using System.Text;
using System.Text.Json;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Lsp;
using NVS.Services.Lsp.Protocol;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Tests;

public sealed class LspClientTests : IAsyncLifetime
{
    private readonly ILanguageService _languageService;
    private readonly MockLspServer _mockServer;
    private readonly LspClient _client;

    public LspClientTests()
    {
        _languageService = Substitute.For<ILanguageService>();
        _languageService.GetLanguageId(Language.CSharp).Returns("csharp");

        _mockServer = new MockLspServer();

        var config = new NVS.Core.Models.Settings.LanguageServerConfig
        {
            Command = "mock-server",
        };

        _client = new LspClient(
            Language.CSharp,
            config,
            _languageService,
            _mockServer.Transport);
    }

    public async Task InitializeAsync()
    {
        // Set up default initialize response
        _mockServer.OnRequest("initialize", _ => new
        {
            capabilities = new
            {
                textDocumentSync = new { openClose = true, change = 1, save = true },
                completionProvider = true,
                hoverProvider = true,
                definitionProvider = true,
                referencesProvider = true,
                documentSymbolProvider = true,
                documentFormattingProvider = true,
            },
        });

        _mockServer.Start();
    }

    public async Task DisposeAsync()
    {
        _mockServer.Dispose();
        try
        {
            await _client.DisposeAsync();
        }
        catch
        {
            // Suppress dispose errors in tests — mock server is already torn down
        }
    }

    // ─── Lifecycle ──────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_ShouldSetIsConnected()
    {
        await _client.InitializeAsync(@"C:\project");

        _client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_ShouldPopulateServerCapabilities()
    {
        await _client.InitializeAsync(@"C:\project");

        _client.ServerCapabilities.Should().NotBeNull();
        _client.ServerCapabilities!.CompletionProvider.Should().BeTrue();
        _client.ServerCapabilities.HoverProvider.Should().BeTrue();
    }

    [Fact]
    public async Task Language_ShouldBeSetFromConstructor()
    {
        _client.Language.Should().Be(Language.CSharp);
    }

    [Fact]
    public async Task ShutdownAsync_ShouldSetIsConnectedFalse()
    {
        _mockServer.OnRequest("shutdown", _ => (object?)null);
        await _client.InitializeAsync(@"C:\project");

        await _client.ShutdownAsync();

        _client.IsConnected.Should().BeFalse();
    }

    // ─── Completions ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCompletionsAsync_ShouldReturnItems()
    {
        _mockServer.OnRequest("textDocument/completion", _ => new[]
        {
            new { label = "ToString", kind = 2, detail = "string ToString()" },
            new { label = "GetHashCode", kind = 2, detail = "int GetHashCode()" },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var position = new Position { Line = 5, Column = 10 };
        var result = await _client.GetCompletionsAsync(doc, position);

        result.Should().HaveCount(2);
        result[0].Label.Should().Be("ToString");
        result[0].Kind.Should().Be(CompletionItemKind.Method);
    }

    [Fact]
    public async Task GetCompletionsAsync_WithCompletionList_ShouldReturnItems()
    {
        _mockServer.OnRequest("textDocument/completion", _ => new
        {
            isIncomplete = false,
            items = new[]
            {
                new { label = "Console", kind = 7, detail = "System.Console" },
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("Console");
        result[0].Kind.Should().Be(CompletionItemKind.Class);
    }

    // ─── Hover ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHoverAsync_ShouldReturnHoverInfo()
    {
        _mockServer.OnRequest("textDocument/hover", _ => new
        {
            contents = new { kind = "markdown", value = "```csharp\nint x\n```" },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetHoverAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.Content.Should().Contain("int x");
    }

    [Fact]
    public async Task GetHoverAsync_WithNullResult_ShouldReturnNull()
    {
        _mockServer.OnRequest("textDocument/hover", _ => (object?)null);

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetHoverAsync(doc, Position.Zero);

        result.Should().BeNull();
    }

    // ─── Definition ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDefinitionAsync_WithSingleLocation_ShouldReturnLocation()
    {
        _mockServer.OnRequest("textDocument/definition", _ => new
        {
            uri = "file:///C:/project/other.cs",
            range = new
            {
                start = new { line = 10, character = 0 },
                end = new { line = 10, character = 15 },
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetDefinitionAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.FilePath.Should().Contain("other.cs");
        result.Range.Start.Line.Should().Be(10);
    }

    [Fact]
    public async Task GetDefinitionAsync_WithLocationArray_ShouldReturnFirst()
    {
        _mockServer.OnRequest("textDocument/definition", _ => new[]
        {
            new
            {
                uri = "file:///C:/project/first.cs",
                range = new
                {
                    start = new { line = 1, character = 0 },
                    end = new { line = 1, character = 5 },
                },
            },
            new
            {
                uri = "file:///C:/project/second.cs",
                range = new
                {
                    start = new { line = 2, character = 0 },
                    end = new { line = 2, character = 5 },
                },
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetDefinitionAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.FilePath.Should().Contain("first.cs");
    }

    // ─── References ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReferencesAsync_ShouldReturnLocations()
    {
        _mockServer.OnRequest("textDocument/references", _ => new[]
        {
            new
            {
                uri = "file:///C:/project/a.cs",
                range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 5 } },
            },
            new
            {
                uri = "file:///C:/project/b.cs",
                range = new { start = new { line = 2, character = 0 }, end = new { line = 2, character = 5 } },
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetReferencesAsync(doc, Position.Zero);

        result.Should().HaveCount(2);
    }

    // ─── Document Symbols ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentSymbolsAsync_ShouldReturnSymbols()
    {
        _mockServer.OnRequest("textDocument/documentSymbol", _ => new[]
        {
            new
            {
                name = "MyClass",
                kind = 5,
                range = new { start = new { line = 0, character = 0 }, end = new { line = 50, character = 0 } },
                selectionRange = new { start = new { line = 0, character = 13 }, end = new { line = 0, character = 20 } },
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetDocumentSymbolsAsync(doc);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("MyClass");
        result[0].Kind.Should().Be(SymbolKind.Class);
    }

    // ─── Formatting ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFormattingEditsAsync_ShouldReturnEdits()
    {
        _mockServer.OnRequest("textDocument/formatting", _ => new[]
        {
            new
            {
                range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
                newText = "     ",
            },
        });

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var result = await _client.GetFormattingEditsAsync(doc);

        result.Should().HaveCount(1);
        result[0].NewText.Should().Be("     ");
    }

    // ─── Diagnostics Notification ───────────────────────────────────────────

    [Fact]
    public async Task DiagnosticsReceived_ShouldFireOnPublishDiagnostics()
    {
        await _client.InitializeAsync(@"C:\project");

        var diagnosticsReceived = new TaskCompletionSource<IReadOnlyList<Diagnostic>>();
        _client.DiagnosticsReceived += (_, diagnostics) => diagnosticsReceived.TrySetResult(diagnostics);

        _mockServer.SendNotification("textDocument/publishDiagnostics", new
        {
            uri = "file:///C:/project/test.cs",
            diagnostics = new[]
            {
                new
                {
                    range = new { start = new { line = 5, character = 0 }, end = new { line = 5, character = 10 } },
                    severity = 1,
                    message = "Use of undefined variable",
                    source = "csharp",
                    code = "CS0103",
                },
            },
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await diagnosticsReceived.Task.WaitAsync(cts.Token);

        result.Should().HaveCount(1);
        result[0].Message.Should().Be("Use of undefined variable");
        result[0].Severity.Should().Be(DiagnosticSeverity.Error);
        result[0].Code.Should().Be("CS0103");
    }

    // ─── Document Notifications ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyDocumentOpened_ShouldSendNotification()
    {
        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        _client.NotifyDocumentOpened(doc);

        // Give the async notification time to be written
        await Task.Delay(100);

        var received = _mockServer.ReceivedNotifications
            .FirstOrDefault(n => n.Method == "textDocument/didOpen");
        received.Should().NotBeNull();
    }

    [Fact]
    public async Task NotifyDocumentChanged_ShouldSendNotification()
    {
        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        _client.NotifyDocumentChanged(doc, "new content");

        await Task.Delay(100);

        var received = _mockServer.ReceivedNotifications
            .FirstOrDefault(n => n.Method == "textDocument/didChange");
        received.Should().NotBeNull();
    }

    [Fact]
    public async Task NotifyDocumentClosed_ShouldSendNotification()
    {
        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        _client.NotifyDocumentClosed(doc);

        await Task.Delay(100);

        var received = _mockServer.ReceivedNotifications
            .FirstOrDefault(n => n.Method == "textDocument/didClose");
        received.Should().NotBeNull();
    }

    [Fact]
    public async Task NotifyDocumentSaved_ShouldSendNotification()
    {
        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        _client.NotifyDocumentSaved(doc);

        await Task.Delay(100);

        var received = _mockServer.ReceivedNotifications
            .FirstOrDefault(n => n.Method == "textDocument/didSave");
        received.Should().NotBeNull();
    }

    // ─── Error Handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendRequest_WithErrorResponse_ShouldThrowLspRequestException()
    {
        _mockServer.OnRequestError("textDocument/hover", -32601, "Method not found");

        await _client.InitializeAsync(@"C:\project");

        var doc = CreateDocument("test.cs");
        var act = () => _client.GetHoverAsync(doc, Position.Zero);

        await act.Should().ThrowAsync<LspRequestException>()
            .WithMessage("*Method not found*");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Document CreateDocument(string name) => new()
    {
        Id = Guid.NewGuid(),
        Path = name,
        Name = name,
        FilePath = $@"C:\project\{name}",
        Language = Language.CSharp,
        Content = "// test content",
        Version = 1,
    };
}

/// <summary>
/// In-memory mock LSP server that communicates via streams.
/// Simulates a language server for testing the LspClient.
/// </summary>
internal sealed class MockLspServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly DuplexStream _clientToServer = new();
    private readonly DuplexStream _serverToClient = new();
    private readonly Dictionary<string, Func<JsonElement?, object?>> _requestHandlers = new();
    private readonly Dictionary<string, (int code, string message)> _errorHandlers = new();
    private readonly List<JsonRpcNotification> _receivedNotifications = [];
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public IReadOnlyList<JsonRpcNotification> ReceivedNotifications => _receivedNotifications;

    /// <summary>
    /// Creates a transport that the LspClient uses.
    /// Client reads from serverToClient, writes to clientToServer.
    /// </summary>
    public JsonRpcTransport Transport => new(
        _serverToClient.GetReadStream(),
        _clientToServer.GetWriteStream());

    public void OnRequest(string method, Func<JsonElement?, object?> handler)
    {
        _requestHandlers[method] = handler;
    }

    public void OnRequestError(string method, int code, string message)
    {
        _errorHandlers[method] = (code, message);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listenTask = ListenAsync(_cts.Token);
    }

    public void SendNotification(string method, object param)
    {
        var notification = new JsonRpcNotification
        {
            Method = method,
            Params = JsonSerializer.SerializeToElement(param, JsonOptions),
        };

        var json = JsonRpcTransport.Serialize(notification);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        var writeStream = _serverToClient.GetWriteStream();
        writeStream.Write(header);
        writeStream.Write(body);
        writeStream.Flush();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var serverTransport = new JsonRpcTransport(
            _clientToServer.GetReadStream(),
            _serverToClient.GetWriteStream());

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await serverTransport.ReadMessageAsync(cancellationToken);
                if (message is null)
                    break;

                switch (message)
                {
                    case JsonRpcRequest request:
                        await HandleRequestAsync(request, serverTransport, cancellationToken);
                        break;
                    case JsonRpcNotification notification:
                        _receivedNotifications.Add(notification);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleRequestAsync(JsonRpcRequest request, JsonRpcTransport serverTransport, CancellationToken cancellationToken)
    {
        JsonRpcResponse response;

        if (_errorHandlers.TryGetValue(request.Method, out var error))
        {
            response = new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = error.code, Message = error.message },
            };
        }
        else if (_requestHandlers.TryGetValue(request.Method, out var handler))
        {
            var result = handler(request.Params);
            response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = result is not null
                    ? JsonSerializer.SerializeToElement(result, JsonOptions)
                    : null,
            };
        }
        else
        {
            response = new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = -32601, Message = $"Method not found: {request.Method}" },
            };
        }

        await serverTransport.WriteMessageAsync(response, cancellationToken);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _clientToServer.Complete();
        _serverToClient.Complete();
        _cts?.Dispose();
    }
}

/// <summary>
/// A duplex stream built on Channel&lt;byte[]&gt; for async-friendly test I/O.
/// Separate read/write stream views avoid the bidirectional issues.
/// </summary>
internal sealed class DuplexStream
{
    private readonly System.Threading.Channels.Channel<byte[]> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<byte[]>();

    private readonly DuplexReadStream _readStream;
    private readonly DuplexWriteStream _writeStream;

    public DuplexStream()
    {
        _readStream = new DuplexReadStream(_channel.Reader);
        _writeStream = new DuplexWriteStream(_channel.Writer);
    }

    public Stream GetReadStream() => _readStream;
    public Stream GetWriteStream() => _writeStream;

    public void Complete() => _channel.Writer.TryComplete();

    private sealed class DuplexWriteStream : Stream
    {
        private readonly System.Threading.Channels.ChannelWriter<byte[]> _writer;

        public DuplexWriteStream(System.Threading.Channels.ChannelWriter<byte[]> writer) => _writer = writer;

        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);
            _writer.TryWrite(data);
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class DuplexReadStream : Stream
    {
        private readonly System.Threading.Channels.ChannelReader<byte[]> _reader;
        private byte[] _currentBuffer = [];
        private int _currentOffset;

        public DuplexReadStream(System.Threading.Channels.ChannelReader<byte[]> reader) => _reader = reader;

        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Synchronous fallback — use ReadAsync when possible
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (_currentOffset >= _currentBuffer.Length)
            {
                if (!await _reader.WaitToReadAsync(cancellationToken))
                    return 0;

                if (!_reader.TryRead(out var data))
                    continue;

                _currentBuffer = data;
                _currentOffset = 0;
            }

            var available = _currentBuffer.Length - _currentOffset;
            var toCopy = Math.Min(available, count);
            Array.Copy(_currentBuffer, _currentOffset, buffer, offset, toCopy);
            _currentOffset += toCopy;
            return toCopy;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_currentOffset >= _currentBuffer.Length)
            {
                if (!await _reader.WaitToReadAsync(cancellationToken))
                    return 0;

                if (!_reader.TryRead(out var data))
                    continue;

                _currentBuffer = data;
                _currentOffset = 0;
            }

            var available = _currentBuffer.Length - _currentOffset;
            var toCopy = Math.Min(available, buffer.Length);
            _currentBuffer.AsMemory(_currentOffset, toCopy).CopyTo(buffer);
            _currentOffset += toCopy;
            return toCopy;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
