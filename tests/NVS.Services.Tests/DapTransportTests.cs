using System.Text;
using System.Text.Json;
using NVS.Core.Debug;
using NVS.Services.Debug;

namespace NVS.Services.Tests;

public sealed class DapTransportTests
{
    // ── Serialization ───────────────────────────────────────────────

    [Fact]
    public void Serialize_Request_ShouldIncludeTypeAndCommand()
    {
        var request = new DapRequest
        {
            Seq = 1,
            Command = "initialize",
            Arguments = JsonSerializer.SerializeToElement(new { clientID = "nvs", adapterID = "coreclr" })
        };

        var json = DapTransport.Serialize(request);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("request");
        doc.RootElement.GetProperty("command").GetString().Should().Be("initialize");
        doc.RootElement.GetProperty("seq").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("arguments").GetProperty("clientID").GetString().Should().Be("nvs");
    }

    [Fact]
    public void Serialize_Response_ShouldIncludeRequestSeqAndSuccess()
    {
        var response = new DapResponse
        {
            Seq = 2,
            RequestSeq = 1,
            Success = true,
            Command = "initialize",
            Body = JsonSerializer.SerializeToElement(new { supportsConfigurationDoneRequest = true })
        };

        var json = DapTransport.Serialize(response);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("response");
        doc.RootElement.GetProperty("request_seq").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("command").GetString().Should().Be("initialize");
    }

    [Fact]
    public void Serialize_Event_ShouldIncludeEventName()
    {
        var evt = new DapEvent
        {
            Seq = 3,
            Event = "stopped",
            Body = JsonSerializer.SerializeToElement(new { reason = "breakpoint", threadId = 1 })
        };

        var json = DapTransport.Serialize(evt);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("type").GetString().Should().Be("event");
        doc.RootElement.GetProperty("event").GetString().Should().Be("stopped");
        doc.RootElement.GetProperty("body").GetProperty("reason").GetString().Should().Be("breakpoint");
    }

    [Fact]
    public void Serialize_Request_WithNullArguments_ShouldOmitArguments()
    {
        var request = new DapRequest { Seq = 1, Command = "configurationDone" };

        var json = DapTransport.Serialize(request);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("arguments", out _).Should().BeFalse();
    }

    // ── Deserialization ─────────────────────────────────────────────

    [Fact]
    public void Deserialize_Request_ShouldReturnDapRequest()
    {
        var json = """{"seq":1,"type":"request","command":"initialize","arguments":{"clientID":"nvs"}}""";

        var msg = DapTransport.Deserialize(json);

        msg.Should().BeOfType<DapRequest>();
        var request = (DapRequest)msg!;
        request.Seq.Should().Be(1);
        request.Command.Should().Be("initialize");
        request.Arguments.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_Response_ShouldReturnDapResponse()
    {
        var json = """{"seq":2,"type":"response","request_seq":1,"success":true,"command":"initialize","body":{"supportsConfigurationDoneRequest":true}}""";

        var msg = DapTransport.Deserialize(json);

        msg.Should().BeOfType<DapResponse>();
        var response = (DapResponse)msg!;
        response.RequestSeq.Should().Be(1);
        response.Success.Should().BeTrue();
        response.Command.Should().Be("initialize");
        response.Body.Should().NotBeNull();
    }

    [Fact]
    public void Deserialize_Event_ShouldReturnDapEvent()
    {
        var json = """{"seq":5,"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""";

        var msg = DapTransport.Deserialize(json);

        msg.Should().BeOfType<DapEvent>();
        var evt = (DapEvent)msg!;
        evt.Event.Should().Be("stopped");
        evt.Body!.Value.GetProperty("reason").GetString().Should().Be("breakpoint");
    }

    [Fact]
    public void Deserialize_FailedResponse_ShouldPreserveMessage()
    {
        var json = """{"seq":3,"type":"response","request_seq":2,"success":false,"command":"launch","message":"File not found"}""";

        var msg = DapTransport.Deserialize(json);

        var response = msg.Should().BeOfType<DapResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Message.Should().Be("File not found");
    }

    [Fact]
    public void Deserialize_UnknownType_ShouldReturnNull()
    {
        var json = """{"seq":1,"type":"unknown","command":"foo"}""";

        var msg = DapTransport.Deserialize(json);

        msg.Should().BeNull();
    }

    // ── Transport Read/Write ────────────────────────────────────────

    [Fact]
    public async Task ReadMessageAsync_ShouldParseContentLengthFramedMessage()
    {
        var body = """{"seq":1,"type":"event","event":"initialized"}""";
        var framed = $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(framed));

        await using var transport = new DapTransport(inputStream, new MemoryStream());
        var msg = await transport.ReadMessageAsync();

        msg.Should().BeOfType<DapEvent>();
        ((DapEvent)msg!).Event.Should().Be("initialized");
    }

    [Fact]
    public async Task WriteMessageAsync_ShouldProduceContentLengthFramedOutput()
    {
        var inputStream = new MemoryStream();
        var outputStream = new MemoryStream();

        await using var transport = new DapTransport(inputStream, outputStream);

        var request = new DapRequest
        {
            Seq = 1,
            Command = "initialize",
            Arguments = JsonSerializer.SerializeToElement(new { clientID = "nvs" })
        };

        await transport.WriteMessageAsync(request);

        outputStream.Position = 0;
        var output = Encoding.UTF8.GetString(outputStream.ToArray());

        output.Should().StartWith("Content-Length: ");
        output.Should().Contain("\r\n\r\n");

        var jsonStart = output.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var json = output[jsonStart..];
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("command").GetString().Should().Be("initialize");
    }

    [Fact]
    public async Task ReadMessageAsync_WithMultipleMessages_ShouldReadSequentially()
    {
        var msg1 = """{"seq":1,"type":"event","event":"initialized"}""";
        var msg2 = """{"seq":2,"type":"event","event":"stopped","body":{"reason":"entry","threadId":1}}""";
        var framed = $"Content-Length: {Encoding.UTF8.GetByteCount(msg1)}\r\n\r\n{msg1}" +
                     $"Content-Length: {Encoding.UTF8.GetByteCount(msg2)}\r\n\r\n{msg2}";
        var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(framed));

        await using var transport = new DapTransport(inputStream, new MemoryStream());

        var first = await transport.ReadMessageAsync();
        first.Should().BeOfType<DapEvent>();
        ((DapEvent)first!).Event.Should().Be("initialized");

        var second = await transport.ReadMessageAsync();
        second.Should().BeOfType<DapEvent>();
        ((DapEvent)second!).Event.Should().Be("stopped");
    }

    [Fact]
    public async Task ReadMessageAsync_EmptyStream_ShouldReturnNull()
    {
        await using var transport = new DapTransport(new MemoryStream(), new MemoryStream());

        var msg = await transport.ReadMessageAsync();
        msg.Should().BeNull();
    }

    [Fact]
    public async Task WriteAndRead_RoundTrip_ShouldPreserveMessage()
    {
        var pipe = new MemoryStream();

        var request = new DapRequest
        {
            Seq = 42,
            Command = "setBreakpoints",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                source = new { path = "/test.cs" },
                breakpoints = new[] { new { line = 10 } }
            })
        };

        await using (var writeTransport = new DapTransport(Stream.Null, new NonClosingStream(pipe)))
        {
            await writeTransport.WriteMessageAsync(request);
        }

        pipe.Position = 0;
        await using var readTransport = new DapTransport(new NonClosingStream(pipe), Stream.Null);
        var msg = await readTransport.ReadMessageAsync();

        var result = msg.Should().BeOfType<DapRequest>().Subject;
        result.Seq.Should().Be(42);
        result.Command.Should().Be("setBreakpoints");
        result.Arguments!.Value.GetProperty("source").GetProperty("path").GetString().Should().Be("/test.cs");
    }

    // ── Protocol Type Serialization ─────────────────────────────────

    [Fact]
    public void DapInitializeRequestArguments_ShouldSerializeCorrectly()
    {
        var args = new DapInitializeRequestArguments
        {
            ClientId = "nvs",
            ClientName = "NVS IDE",
            AdapterId = "coreclr"
        };

        var json = JsonSerializer.Serialize(args, DapTransport.JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("clientID").GetString().Should().Be("nvs");
        doc.RootElement.GetProperty("clientName").GetString().Should().Be("NVS IDE");
        doc.RootElement.GetProperty("adapterID").GetString().Should().Be("coreclr");
        doc.RootElement.GetProperty("linesStartAt1").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DapLaunchRequestArguments_ShouldSerializeCorrectly()
    {
        var args = new DapLaunchRequestArguments
        {
            Program = "/bin/myapp",
            Args = ["--verbose"],
            Cwd = "/home/user",
            StopAtEntry = true
        };

        var json = JsonSerializer.Serialize(args, DapTransport.JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("program").GetString().Should().Be("/bin/myapp");
        doc.RootElement.GetProperty("args")[0].GetString().Should().Be("--verbose");
        doc.RootElement.GetProperty("stopAtEntry").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DapStoppedEventBody_ShouldDeserializeCorrectly()
    {
        var json = """{"reason":"breakpoint","threadId":1,"allThreadsStopped":true}""";

        var body = JsonSerializer.Deserialize<DapStoppedEventBody>(json, DapTransport.JsonOptions);

        body.Should().NotBeNull();
        body!.Reason.Should().Be("breakpoint");
        body.ThreadId.Should().Be(1);
        body.AllThreadsStopped.Should().BeTrue();
    }

    [Fact]
    public void DapStackFrame_ShouldDeserializeWithSource()
    {
        var json = """{"id":1,"name":"Main","source":{"name":"Program.cs","path":"/src/Program.cs"},"line":10,"column":5}""";

        var frame = JsonSerializer.Deserialize<DapStackFrame>(json, DapTransport.JsonOptions);

        frame.Should().NotBeNull();
        frame!.Name.Should().Be("Main");
        frame.Source!.Path.Should().Be("/src/Program.cs");
        frame.Line.Should().Be(10);
    }

    [Fact]
    public void DapVariable_ShouldDeserializeWithNestedReference()
    {
        var json = """{"name":"myList","value":"Count = 3","type":"List<int>","variablesReference":42}""";

        var variable = JsonSerializer.Deserialize<DapVariable>(json, DapTransport.JsonOptions);

        variable.Should().NotBeNull();
        variable!.Name.Should().Be("myList");
        variable.Value.Should().Be("Count = 3");
        variable.Type.Should().Be("List<int>");
        variable.VariablesReference.Should().Be(42);
    }

    [Fact]
    public void DapSetBreakpointsArguments_ShouldSerializeCorrectly()
    {
        var args = new DapSetBreakpointsArguments
        {
            Source = new DapSource { Path = "/src/Program.cs" },
            Breakpoints = [new DapSourceBreakpoint { Line = 10 }, new DapSourceBreakpoint { Line = 20, Condition = "x > 5" }]
        };

        var json = JsonSerializer.Serialize(args, DapTransport.JsonOptions);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("source").GetProperty("path").GetString().Should().Be("/src/Program.cs");
        doc.RootElement.GetProperty("breakpoints").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("breakpoints")[1].GetProperty("condition").GetString().Should().Be("x > 5");
    }

    [Fact]
    public void DapOutputEventBody_ShouldDeserializeCorrectly()
    {
        var json = """{"category":"stdout","output":"Hello World\n"}""";

        var body = JsonSerializer.Deserialize<DapOutputEventBody>(json, DapTransport.JsonOptions);

        body.Should().NotBeNull();
        body!.Category.Should().Be("stdout");
        body.Output.Should().Be("Hello World\n");
    }
}

file sealed class NonClosingStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.ReadAsync(buffer, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.WriteAsync(buffer, cancellationToken);
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    protected override void Dispose(bool disposing) { /* don't dispose inner */ }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
