using System.Text;
using System.Text.Json;

namespace NVS.Services.Lsp;

/// <summary>
/// Handles reading and writing LSP-framed JSON-RPC messages over streams.
/// LSP framing uses Content-Length headers: "Content-Length: N\r\n\r\n{json}"
/// </summary>
public sealed class JsonRpcTransport : IAsyncDisposable
{
    private readonly TextReader? _reader;
    private readonly Stream? _input;
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

    /// <summary>
    /// Creates a transport using a TextReader for input (preferred for process stdout
    /// to avoid buffering conflicts between StreamReader and BaseStream).
    /// </summary>
    public JsonRpcTransport(TextReader reader, Stream output)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>
    /// Creates a transport using raw streams (for testing or non-process streams).
    /// </summary>
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
            // Run all synchronous pipe reads on a thread-pool thread to avoid
            // issues with async-over-sync on non-overlapped pipe handles.
            var result = await Task.Run(() => ReadMessageSync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        finally
        {
            _readLock.Release();
        }
    }

    private JsonRpcMessage? ReadMessageSync(CancellationToken cancellationToken)
    {
        var contentLength = ReadContentLengthSync(cancellationToken);
        if (contentLength < 0)
            return null;

        string json;
        if (_reader is not null)
        {
            // Read body as characters via TextReader (Content-Length is bytes, but for
            // UTF-8 JSON with ASCII-safe escaping, bytes ≈ chars). Read char-by-char
            // to handle the rare case of multi-byte characters correctly.
            var chars = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = _reader.Read(chars, totalRead, contentLength - totalRead);
                if (read == 0)
                    return null;
                totalRead += read;
            }
            json = new string(chars, 0, totalRead);

            // If the UTF-8 byte length doesn't match Content-Length, we over-read.
            // Re-read the remaining bytes that belong to the next message header.
            var actualBytes = Encoding.UTF8.GetByteCount(json);
            if (actualBytes < contentLength)
            {
                // Multi-byte chars: we read too few bytes. Read more chars.
                var remaining = contentLength - actualBytes;
                var extra = new char[remaining];
                var extraRead = 0;
                while (extraRead < remaining)
                {
                    var r = _reader.Read(extra, extraRead, remaining - extraRead);
                    if (r == 0) break;
                    extraRead += r;
                }
                json += new string(extra, 0, extraRead);
            }
        }
        else
        {
            var body = new byte[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = _input!.Read(body, totalRead, contentLength - totalRead);
                if (read == 0)
                    return null;
                totalRead += read;
            }
            json = Encoding.UTF8.GetString(body);
        }

        return Deserialize(json);
    }

    private int ReadContentLengthSync(CancellationToken cancellationToken)
    {
        var contentLength = -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = ReadLineSync();
            if (line is null)
                return -1;

            if (line.Length == 0)
                break;

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line["Content-Length:".Length..].Trim();
                if (int.TryParse(value, out var parsed))
                    contentLength = parsed;
            }
        }

        return contentLength;
    }

    private string? ReadLineSync()
    {
        if (_reader is not null)
        {
            // Use TextReader.ReadLine() which handles the StreamReader buffer correctly
            return _reader.ReadLine();
        }

        // Fallback: raw byte-by-byte reading for Stream-based transport
        var buffer = new List<byte>(128);
        while (true)
        {
            var b = _input!.ReadByte();
            if (b == -1)
                return null;

            if (b == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == (byte)'\r')
                    buffer.RemoveAt(buffer.Count - 1);

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add((byte)b);
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
