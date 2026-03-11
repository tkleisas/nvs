using System.Buffers;
using System.Text;
using System.Text.Json;

namespace NVS.Services.Lsp;

/// <summary>
/// Handles reading and writing LSP-framed JSON-RPC messages over streams.
/// LSP framing uses Content-Length headers: "Content-Length: N\r\n\r\n{json}"
/// </summary>
public sealed class JsonRpcTransport : IAsyncDisposable
{
    private static readonly byte[] ContentLengthHeader = "Content-Length: "u8.ToArray();
    private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
    private static readonly byte[] LineTerminator = "\r\n"u8.ToArray();

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _readLock = new(1, 1);
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public JsonRpcTransport(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Reads one JSON-RPC message from the input stream.
    /// Returns null if the stream is closed.
    /// </summary>
    public async Task<JsonRpcMessage?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _readLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contentLength = await ReadContentLengthAsync(cancellationToken).ConfigureAwait(false);
            if (contentLength < 0)
                return null;

            var body = new byte[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await _input.ReadAsync(
                    body.AsMemory(totalRead, contentLength - totalRead),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                    return null;

                totalRead += read;
            }

            var json = Encoding.UTF8.GetString(body);
            return Deserialize(json);
        }
        finally
        {
            _readLock.Release();
        }
    }

    /// <summary>
    /// Writes a JSON-RPC message to the output stream with LSP framing.
    /// </summary>
    public async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        var json = Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<int> ReadContentLengthAsync(CancellationToken cancellationToken)
    {
        var contentLength = -1;

        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return -1;

            if (line.Length == 0)
            {
                // Empty line = end of headers
                break;
            }

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out var parsed))
                    contentLength = parsed;
            }
            // Other headers (e.g., Content-Type) are ignored per LSP spec
        }

        return contentLength;
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(128);

        while (true)
        {
            var b = new byte[1];
            var read = await _input.ReadAsync(b, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;

            if (b[0] == (byte)'\n')
            {
                // Strip trailing \r if present
                if (buffer.Count > 0 && buffer[^1] == (byte)'\r')
                    buffer.RemoveAt(buffer.Count - 1);

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add(b[0]);
        }
    }

    internal static string Serialize(JsonRpcMessage message)
    {
        return message switch
        {
            JsonRpcRequest request => JsonSerializer.Serialize(request, JsonOptions),
            JsonRpcResponse response => JsonSerializer.Serialize(response, JsonOptions),
            JsonRpcNotification notification => JsonSerializer.Serialize(notification, JsonOptions),
            _ => throw new ArgumentException($"Unknown message type: {message.GetType()}")
        };
    }

    internal static JsonRpcMessage? Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var hasId = root.TryGetProperty("id", out var idElement);
        var hasMethod = root.TryGetProperty("method", out var methodElement);

        if (hasMethod && hasId)
        {
            // Request: has both id and method
            return new JsonRpcRequest
            {
                Id = ParseId(idElement),
                Method = methodElement.GetString()!,
                Params = root.TryGetProperty("params", out var p) ? CloneElement(p) : null,
            };
        }

        if (hasMethod && !hasId)
        {
            // Notification: has method but no id
            return new JsonRpcNotification
            {
                Method = methodElement.GetString()!,
                Params = root.TryGetProperty("params", out var p) ? CloneElement(p) : null,
            };
        }

        if (hasId && !hasMethod)
        {
            // Response: has id but no method
            JsonRpcError? error = null;
            if (root.TryGetProperty("error", out var errorElement))
            {
                error = new JsonRpcError
                {
                    Code = errorElement.GetProperty("code").GetInt32(),
                    Message = errorElement.GetProperty("message").GetString() ?? string.Empty,
                    Data = errorElement.TryGetProperty("data", out var d) ? CloneElement(d) : null,
                };
            }

            return new JsonRpcResponse
            {
                Id = ParseId(idElement),
                Result = root.TryGetProperty("result", out var r) ? CloneElement(r) : null,
                Error = error,
            };
        }

        return null;
    }

    private static object ParseId(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String => element.GetString()!,
            _ => element.GetRawText(),
        };
    }

    private static JsonElement CloneElement(JsonElement element)
    {
        return element.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _writeLock.Dispose();
        _readLock.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Base class for JSON-RPC messages.
/// </summary>
public abstract record JsonRpcMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";
}

/// <summary>
/// A JSON-RPC request (has id + method).
/// </summary>
public sealed record JsonRpcRequest : JsonRpcMessage
{
    public required object Id { get; init; }
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}

/// <summary>
/// A JSON-RPC notification (has method, no id).
/// </summary>
public sealed record JsonRpcNotification : JsonRpcMessage
{
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}

/// <summary>
/// A JSON-RPC response (has id, result or error).
/// </summary>
public sealed record JsonRpcResponse : JsonRpcMessage
{
    public required object Id { get; init; }
    public JsonElement? Result { get; init; }
    public JsonRpcError? Error { get; init; }
}

/// <summary>
/// JSON-RPC error object.
/// </summary>
public sealed record JsonRpcError
{
    public required int Code { get; init; }
    public required string Message { get; init; }
    public JsonElement? Data { get; init; }
}
