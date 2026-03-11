using System.Text;
using System.Text.Json;
using NVS.Services.Lsp;

namespace NVS.Services.Tests;

public sealed class JsonRpcTransportTests
{
    // ─── Serialization ──────────────────────────────────────────────────────

    [Fact]
    public void Serialize_Request_ShouldIncludeAllFields()
    {
        var request = new JsonRpcRequest
        {
            Id = 1L,
            Method = "textDocument/completion",
            Params = JsonSerializer.SerializeToElement(new { uri = "file:///test.cs" }),
        };

        var json = JsonRpcTransport.Serialize(request);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("id").GetInt64().Should().Be(1);
        root.GetProperty("method").GetString().Should().Be("textDocument/completion");
        root.GetProperty("params").GetProperty("uri").GetString().Should().Be("file:///test.cs");
    }

    [Fact]
    public void Serialize_Notification_ShouldNotHaveId()
    {
        var notification = new JsonRpcNotification
        {
            Method = "initialized",
        };

        var json = JsonRpcTransport.Serialize(notification);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("initialized");
        root.TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_Response_ShouldIncludeResult()
    {
        var response = new JsonRpcResponse
        {
            Id = 42L,
            Result = JsonSerializer.SerializeToElement(new { capabilities = new { } }),
        };

        var json = JsonRpcTransport.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("id").GetInt64().Should().Be(42);
        root.GetProperty("result").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void Serialize_ResponseWithError_ShouldIncludeError()
    {
        var response = new JsonRpcResponse
        {
            Id = 1L,
            Error = new JsonRpcError
            {
                Code = -32600,
                Message = "Invalid Request",
            },
        };

        var json = JsonRpcTransport.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32600);
        root.GetProperty("error").GetProperty("message").GetString().Should().Be("Invalid Request");
    }

    // ─── Deserialization ────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_Request_ShouldParseCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"rootUri":"file:///tmp"}}""";

        var message = JsonRpcTransport.Deserialize(json);

        message.Should().BeOfType<JsonRpcRequest>();
        var request = (JsonRpcRequest)message!;
        request.Method.Should().Be("initialize");
        request.Params.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_Notification_ShouldParseCorrectly()
    {
        var json = """{"jsonrpc":"2.0","method":"textDocument/publishDiagnostics","params":{}}""";

        var message = JsonRpcTransport.Deserialize(json);

        message.Should().BeOfType<JsonRpcNotification>();
        var notification = (JsonRpcNotification)message!;
        notification.Method.Should().Be("textDocument/publishDiagnostics");
    }

    [Fact]
    public void Deserialize_SuccessResponse_ShouldParseCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":1,"result":{"capabilities":{}}}""";

        var message = JsonRpcTransport.Deserialize(json);

        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message!;
        response.Result.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public void Deserialize_ErrorResponse_ShouldParseCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Method not found"}}""";

        var message = JsonRpcTransport.Deserialize(json);

        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message!;
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601);
        response.Error.Message.Should().Be("Method not found");
    }

    [Fact]
    public void Deserialize_ResponseWithStringId_ShouldParseCorrectly()
    {
        var json = """{"jsonrpc":"2.0","id":"abc-123","result":null}""";

        var message = JsonRpcTransport.Deserialize(json);

        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message!;
        response.Id.Should().Be("abc-123");
    }

    // ─── Round-Trip Framing ─────────────────────────────────────────────────

    [Fact]
    public async Task WriteAndRead_Request_ShouldRoundTrip()
    {
        using var stream = new MemoryStream();

        var original = new JsonRpcRequest
        {
            Id = 1L,
            Method = "test/method",
            Params = JsonSerializer.SerializeToElement(new { key = "value" }),
        };

        // Write
        await using (var writeTransport = new JsonRpcTransport(Stream.Null, stream))
        {
            await writeTransport.WriteMessageAsync(original);
        }

        // Read
        stream.Position = 0;
        await using var readTransport = new JsonRpcTransport(stream, Stream.Null);
        var message = await readTransport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcRequest>();
        var request = (JsonRpcRequest)message!;
        request.Method.Should().Be("test/method");
    }

    [Fact]
    public async Task WriteAndRead_MultipleMessages_ShouldRoundTrip()
    {
        using var stream = new MemoryStream();

        await using (var writeTransport = new JsonRpcTransport(Stream.Null, stream))
        {
            await writeTransport.WriteMessageAsync(new JsonRpcRequest { Id = 1L, Method = "first" });
            await writeTransport.WriteMessageAsync(new JsonRpcRequest { Id = 2L, Method = "second" });
            await writeTransport.WriteMessageAsync(new JsonRpcNotification { Method = "third" });
        }

        stream.Position = 0;
        await using var readTransport = new JsonRpcTransport(stream, Stream.Null);

        var msg1 = await readTransport.ReadMessageAsync() as JsonRpcRequest;
        var msg2 = await readTransport.ReadMessageAsync() as JsonRpcRequest;
        var msg3 = await readTransport.ReadMessageAsync() as JsonRpcNotification;

        msg1!.Method.Should().Be("first");
        msg2!.Method.Should().Be("second");
        msg3!.Method.Should().Be("third");
    }

    [Fact]
    public async Task ReadMessage_WithContentLengthHeader_ShouldParseBody()
    {
        var json = """{"jsonrpc":"2.0","method":"test"}""";
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {body.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        using var stream = new MemoryStream([.. headerBytes, .. body]);
        await using var transport = new JsonRpcTransport(stream, Stream.Null);

        var message = await transport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcNotification>();
        ((JsonRpcNotification)message!).Method.Should().Be("test");
    }

    [Fact]
    public async Task ReadMessage_WithExtraHeaders_ShouldIgnoreUnknownHeaders()
    {
        var json = """{"jsonrpc":"2.0","id":1,"method":"test"}""";
        var body = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Type: application/vscode-jsonrpc; charset=utf-8\r\nContent-Length: {body.Length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);

        using var stream = new MemoryStream([.. headerBytes, .. body]);
        await using var transport = new JsonRpcTransport(stream, Stream.Null);

        var message = await transport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcRequest>();
    }

    [Fact]
    public async Task ReadMessage_EmptyStream_ShouldReturnNull()
    {
        using var stream = new MemoryStream([]);
        await using var transport = new JsonRpcTransport(stream, Stream.Null);

        var message = await transport.ReadMessageAsync();

        message.Should().BeNull();
    }

    [Fact]
    public async Task WriteMessage_ShouldAddContentLengthHeader()
    {
        using var stream = new MemoryStream();

        await using (var transport = new JsonRpcTransport(Stream.Null, stream))
        {
            await transport.WriteMessageAsync(new JsonRpcNotification { Method = "test" });
        }

        var output = Encoding.UTF8.GetString(stream.ToArray());

        output.Should().StartWith("Content-Length: ");
        output.Should().Contain("\r\n\r\n");
    }

    [Fact]
    public async Task WriteAndRead_Response_ShouldRoundTrip()
    {
        using var stream = new MemoryStream();

        var original = new JsonRpcResponse
        {
            Id = 5L,
            Result = JsonSerializer.SerializeToElement(new { completions = new[] { "foo", "bar" } }),
        };

        await using (var writeTransport = new JsonRpcTransport(Stream.Null, stream))
        {
            await writeTransport.WriteMessageAsync(original);
        }

        stream.Position = 0;
        await using var readTransport = new JsonRpcTransport(stream, Stream.Null);
        var message = await readTransport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcResponse>();
        var response = (JsonRpcResponse)message!;
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteAndRead_Notification_ShouldRoundTrip()
    {
        using var stream = new MemoryStream();

        await using (var writeTransport = new JsonRpcTransport(Stream.Null, stream))
        {
            await writeTransport.WriteMessageAsync(new JsonRpcNotification
            {
                Method = "textDocument/didOpen",
                Params = JsonSerializer.SerializeToElement(new { textDocument = new { uri = "file:///test.cs" } }),
            });
        }

        stream.Position = 0;
        await using var readTransport = new JsonRpcTransport(stream, Stream.Null);
        var message = await readTransport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcNotification>();
        var notification = (JsonRpcNotification)message!;
        notification.Method.Should().Be("textDocument/didOpen");
        notification.Params.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteAndRead_UnicodeContent_ShouldRoundTrip()
    {
        using var stream = new MemoryStream();

        await using (var writeTransport = new JsonRpcTransport(Stream.Null, stream))
        {
            await writeTransport.WriteMessageAsync(new JsonRpcNotification
            {
                Method = "test",
                Params = JsonSerializer.SerializeToElement(new { text = "日本語テスト 🚀" }),
            });
        }

        stream.Position = 0;
        await using var readTransport = new JsonRpcTransport(stream, Stream.Null);
        var message = await readTransport.ReadMessageAsync();

        message.Should().BeOfType<JsonRpcNotification>();
        var notification = (JsonRpcNotification)message!;
        notification.Params!.Value.GetProperty("text").GetString().Should().Be("日本語テスト 🚀");
    }
}
