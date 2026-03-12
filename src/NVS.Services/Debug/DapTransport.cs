using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NVS.Core.Debug;

namespace NVS.Services.Debug;

/// <summary>
/// Transport layer for the Debug Adapter Protocol.
/// Uses Content-Length header framing (identical to LSP) but with DAP message format.
/// </summary>
public sealed class DapTransport : IAsyncDisposable
{
    private static readonly byte[] ContentLengthHeader = "Content-Length: "u8.ToArray();
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new DapMessageConverter() }
    };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public DapTransport(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public async Task<DapMessage?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contentLength = await ReadContentLengthAsync(cancellationToken).ConfigureAwait(false);
            if (contentLength <= 0) return null;

            var buffer = ArrayPool<byte>.Shared.Rent(contentLength);
            try
            {
                var totalRead = 0;
                while (totalRead < contentLength)
                {
                    var bytesRead = await _input.ReadAsync(
                        buffer.AsMemory(totalRead, contentLength - totalRead),
                        cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0) return null;
                    totalRead += bytesRead;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, contentLength);
                return JsonSerializer.Deserialize<DapMessage>(json, JsonOptions);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            _readLock.Release();
        }
    }

    public async Task WriteMessageAsync(DapMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var json = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
        var contentBytes = Encoding.UTF8.GetBytes(json);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = Encoding.ASCII.GetBytes($"Content-Length: {contentBytes.Length}\r\n\r\n");
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(contentBytes, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<int> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        // Read lines until we get Content-Length header, then consume blank line
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return -1;

            if (line.Length == 0)
                continue; // skip blank lines between messages

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var valueStr = line["Content-Length:".Length..].Trim();
                if (!int.TryParse(valueStr, out var length))
                    return -1;

                // Consume remaining headers until blank line
                while (true)
                {
                    var headerLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (headerLine is null || headerLine.Length == 0)
                        break;
                }

                return length;
            }
        }
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];

        while (true)
        {
            var bytesRead = await _input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) return null;

            var ch = (char)buffer[0];
            if (ch == '\n')
            {
                // Remove trailing \r if present
                if (sb.Length > 0 && sb[^1] == '\r')
                    sb.Length--;
                return sb.ToString();
            }

            sb.Append(ch);
        }
    }

    // Serialization helpers for building messages
    public static string Serialize(DapMessage message)
    {
        return JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
    }

    public static DapMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<DapMessage>(json, JsonOptions);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _readLock.Dispose();
        _writeLock.Dispose();
        await _input.DisposeAsync().ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Custom JSON converter to handle polymorphic DapMessage deserialization.
/// Reads the "type" field to determine which concrete type to deserialize.
/// </summary>
internal sealed class DapMessageConverter : JsonConverter<DapMessage>
{
    public override DapMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
            return null;

        var messageType = typeProp.GetString();

        return messageType switch
        {
            "request" => DeserializeRequest(root),
            "response" => DeserializeResponse(root),
            "event" => DeserializeEvent(root),
            _ => null
        };
    }

    public override void Write(Utf8JsonWriter writer, DapMessage value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static DapRequest DeserializeRequest(JsonElement root)
    {
        return new DapRequest
        {
            Seq = root.TryGetProperty("seq", out var seq) ? seq.GetInt32() : 0,
            Command = root.GetProperty("command").GetString()!,
            Arguments = root.TryGetProperty("arguments", out var args)
                ? args.Clone()
                : null
        };
    }

    private static DapResponse DeserializeResponse(JsonElement root)
    {
        return new DapResponse
        {
            Seq = root.TryGetProperty("seq", out var seq) ? seq.GetInt32() : 0,
            RequestSeq = root.TryGetProperty("request_seq", out var rseq) ? rseq.GetInt32() : 0,
            Success = root.TryGetProperty("success", out var success) && success.GetBoolean(),
            Command = root.GetProperty("command").GetString()!,
            Message = root.TryGetProperty("message", out var msg) ? msg.GetString() : null,
            Body = root.TryGetProperty("body", out var body) ? body.Clone() : null
        };
    }

    private static DapEvent DeserializeEvent(JsonElement root)
    {
        return new DapEvent
        {
            Seq = root.TryGetProperty("seq", out var seq) ? seq.GetInt32() : 0,
            Event = root.GetProperty("event").GetString()!,
            Body = root.TryGetProperty("body", out var body) ? body.Clone() : null
        };
    }
}
