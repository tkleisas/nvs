using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Lsp;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Tests;

public sealed class LspSessionManagerTests : IAsyncDisposable
{
    private readonly ILspClientFactory _factory;
    private readonly LspSessionManager _manager;

    public LspSessionManagerTests()
    {
        _factory = Substitute.For<ILspClientFactory>();
        _manager = new LspSessionManager(_factory);
        _manager.SetRootPath(@"C:\project");
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
    }

    // ─── Client Lifecycle ───────────────────────────────────────────────────

    [Fact]
    public async Task GetClientAsync_WithConfiguredLanguage_ShouldReturnClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.GetClientAsync(doc);

        result.Should().BeSameAs(mockClient);
    }

    [Fact]
    public async Task GetClientAsync_WithUnknownLanguage_ShouldReturnNull()
    {
        var doc = CreateDocument("readme.txt", Language.Unknown);
        var result = await _manager.GetClientAsync(doc);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetClientAsync_CalledTwice_ShouldReuseSameClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc1 = CreateDocument("a.cs", Language.CSharp);
        var doc2 = CreateDocument("b.cs", Language.CSharp);

        var result1 = await _manager.GetClientAsync(doc1);
        var result2 = await _manager.GetClientAsync(doc2);

        result1.Should().BeSameAs(result2);
        await _factory.Received(1).CreateClientAsync(Language.CSharp, @"C:\project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetClientAsync_DifferentLanguages_ShouldCreateSeparateClients()
    {
        var csClient = CreateMockClient(Language.CSharp);
        var tsClient = CreateMockClient(Language.TypeScript);
        SetupFactory(Language.CSharp, csClient);
        SetupFactory(Language.TypeScript, tsClient);

        var csDocs = CreateDocument("a.cs", Language.CSharp);
        var tsDocs = CreateDocument("app.ts", Language.TypeScript);

        var result1 = await _manager.GetClientAsync(csDocs);
        var result2 = await _manager.GetClientAsync(tsDocs);

        result1.Should().BeSameAs(csClient);
        result2.Should().BeSameAs(tsClient);
    }

    [Fact]
    public async Task GetClientAsync_WhenFactoryReturnsNull_ShouldReturnNull()
    {
        _factory.CreateClientAsync(Language.CSharp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ILspClient?>(null));

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.GetClientAsync(doc);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetClientAsync_WhenFactoryThrows_ShouldPropagateAndRetry()
    {
        var callCount = 0;
        var mockClient = CreateMockClient(Language.CSharp);

        _factory.CreateClientAsync(Language.CSharp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Server not found");
                return Task.FromResult<ILspClient?>(mockClient);
            });

        var doc = CreateDocument("test.cs", Language.CSharp);

        // First call should throw
        var act = () => _manager.GetClientAsync(doc);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Second call should retry and succeed
        var result = await _manager.GetClientAsync(doc);
        result.Should().BeSameAs(mockClient);
    }

    // ─── Feature Delegation ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCompletionsAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        var expectedCompletions = new List<CompletionItem>
        {
            new() { Label = "ToString", Kind = CompletionItemKind.Method },
        };
        mockClient.GetCompletionsAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedCompletions);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var pos = new Position { Line = 1, Column = 5 };

        var result = await _manager.GetCompletionsAsync(doc, pos);

        result.Should().BeEquivalentTo(expectedCompletions);
    }

    [Fact]
    public async Task GetCompletionsAsync_WithNoClient_ShouldReturnEmpty()
    {
        var doc = CreateDocument("readme.txt", Language.Unknown);
        var pos = new Position { Line = 0, Column = 0 };

        var result = await _manager.GetCompletionsAsync(doc, pos);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDefinitionAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        var expectedLocation = new Location
        {
            FilePath = @"C:\project\Other.cs",
            Range = new Range { Start = new Position { Line = 10, Column = 0 }, End = new Position { Line = 10, Column = 5 } },
        };
        mockClient.GetDefinitionAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(expectedLocation);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var pos = new Position { Line = 5, Column = 10 };

        var result = await _manager.GetDefinitionAsync(doc, pos);

        result.Should().BeEquivalentTo(expectedLocation);
    }

    [Fact]
    public async Task GetDefinitionAsync_WithNoClient_ShouldReturnNull()
    {
        var doc = CreateDocument("readme.txt", Language.Unknown);
        var result = await _manager.GetDefinitionAsync(doc, Position.Zero);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetReferencesAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        var expectedLocations = new List<Location>
        {
            new() { FilePath = @"C:\project\A.cs", Range = Range.Empty },
            new() { FilePath = @"C:\project\B.cs", Range = Range.Empty },
        };
        mockClient.GetReferencesAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(expectedLocations);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.GetReferencesAsync(doc, Position.Zero);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetHoverAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        var hoverInfo = new HoverInfo { Content = "string ToString()" };
        mockClient.GetHoverAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(hoverInfo);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.GetHoverAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.Content.Should().Be("string ToString()");
    }

    [Fact]
    public async Task FormatDocumentAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        var edits = new List<TextEdit>
        {
            new() { Range = Range.Empty, NewText = "    " },
        };
        mockClient.GetFormattingEditsAsync(Arg.Any<Document>(), Arg.Any<CancellationToken>())
            .Returns(edits);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.FormatDocumentAsync(doc);

        result.Should().HaveCount(1);
    }

    // ─── Document Notifications ─────────────────────────────────────────────

    [Fact]
    public async Task NotifyDocumentOpened_ShouldForwardToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        // Must get client first to initialize it
        await _manager.GetClientAsync(doc);

        _manager.NotifyDocumentOpened(doc);

        mockClient.Received(1).NotifyDocumentOpened(doc);
    }

    [Fact]
    public async Task NotifyDocumentChanged_ShouldForwardToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        _manager.NotifyDocumentChanged(doc, "new content");

        mockClient.Received(1).NotifyDocumentChanged(doc, "new content");
    }

    [Fact]
    public async Task NotifyDocumentClosed_ShouldForwardToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        _manager.NotifyDocumentClosed(doc);

        mockClient.Received(1).NotifyDocumentClosed(doc);
    }

    [Fact]
    public async Task NotifyDocumentSaved_ShouldForwardToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        _manager.NotifyDocumentSaved(doc);

        mockClient.Received(1).NotifyDocumentSaved(doc);
    }

    [Fact]
    public void NotifyDocumentOpened_WithNoActiveClient_ShouldNotThrow()
    {
        var doc = CreateDocument("test.cs", Language.CSharp);
        var act = () => _manager.NotifyDocumentOpened(doc);
        act.Should().NotThrow();
    }

    // ─── Diagnostics Relay ──────────────────────────────────────────────────

    [Fact]
    public async Task DiagnosticsChanged_ShouldRelayFromClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        DocumentDiagnosticsEventArgs? receivedArgs = null;
        _manager.DiagnosticsChanged += (_, args) => receivedArgs = args;

        var diagnosticArgs = new DocumentDiagnosticsEventArgs
        {
            DocumentUri = "file:///C:/project/test.cs",
            Diagnostics = [new Diagnostic
            {
                Message = "Error here",
                Severity = DiagnosticSeverity.Error,
                Range = Range.Empty,
            }],
        };

        // Simulate the client raising DiagnosticsReceived
        mockClient.DiagnosticsReceived += Raise.EventWith(mockClient, diagnosticArgs);

        receivedArgs.Should().NotBeNull();
        receivedArgs!.DocumentUri.Should().Be("file:///C:/project/test.cs");
        receivedArgs.Diagnostics.Should().HaveCount(1);
        receivedArgs.Diagnostics[0].Message.Should().Be("Error here");
    }

    // ─── Disposal ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ShouldDisposeAllClients()
    {
        var mockClient = Substitute.For<ILspClient, IAsyncDisposable>();
        mockClient.Language.Returns(Language.CSharp);
        mockClient.IsConnected.Returns(true);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        await _manager.DisposeAsync();

        await ((IAsyncDisposable)mockClient).Received(1).DisposeAsync();
    }

    [Fact]
    public async Task GetClientAsync_AfterDispose_ShouldThrow()
    {
        await _manager.DisposeAsync();

        var doc = CreateDocument("test.cs", Language.CSharp);
        var act = () => _manager.GetClientAsync(doc);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ─── Root Path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClientAsync_WithoutRootPath_ShouldUseDocumentPath()
    {
        var manager = new LspSessionManager(_factory);
        var mockClient = CreateMockClient(Language.CSharp);

        _factory.CreateClientAsync(Language.CSharp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ILspClient?>(mockClient));

        var docPath = Path.Combine(Path.GetTempPath(), "other", "test.cs");
        var expectedRoot = Path.GetDirectoryName(docPath)!;
        var doc = CreateDocument("test.cs", Language.CSharp, docPath);
        await manager.GetClientAsync(doc);

        await _factory.Received(1).CreateClientAsync(Language.CSharp, expectedRoot, Arg.Any<CancellationToken>());

        await manager.DisposeAsync();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ILspClient CreateMockClient(Language language)
    {
        var client = Substitute.For<ILspClient>();
        client.Language.Returns(language);
        client.IsConnected.Returns(true);
        return client;
    }

    private void SetupFactory(Language language, ILspClient client)
    {
        _factory.CreateClientAsync(language, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ILspClient?>(client));
    }

    private static Document CreateDocument(string name, Language language, string? filePath = null)
    {
        var path = filePath ?? $@"C:\project\{name}";
        return new Document
        {
            Id = Guid.NewGuid(),
            Path = path,
            Name = name,
            FilePath = path,
            Language = language,
            Content = "// test content",
        };
    }

    // ─── Code Actions ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCodeActionsAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var expectedActions = new List<CodeAction>
        {
            new() { Title = "Add using", Kind = "quickfix" },
        };

        mockClient.GetCodeActionsAsync(
            Arg.Any<Document>(),
            Arg.Any<Range>(),
            Arg.Any<IReadOnlyList<Diagnostic>>(),
            Arg.Any<CancellationToken>())
            .Returns(expectedActions);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var range = new Range
        {
            Start = new Position { Line = 0, Column = 0 },
            End = new Position { Line = 1, Column = 0 },
        };

        var result = await _manager.GetCodeActionsAsync(doc, range, []);

        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Add using");
    }

    [Fact]
    public async Task GetCodeActionsAsync_WithNoClient_ShouldReturnEmpty()
    {
        _factory.CreateClientAsync(Arg.Any<Language>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ILspClient?>(null));

        var doc = CreateDocument("test.cs", Language.CSharp);
        var range = new Range
        {
            Start = Position.Zero,
            End = new Position { Line = 1, Column = 0 },
        };

        var result = await _manager.GetCodeActionsAsync(doc, range, []);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyWorkspaceEditAsync_ShouldDelegateToClient()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var edit = new WorkspaceEdit
        {
            Changes = new Dictionary<string, IReadOnlyList<TextEdit>>
            {
                [@"C:\project\test.cs"] =
                [
                    new TextEdit
                    {
                        Range = new Range
                        {
                            Start = Position.Zero,
                            End = Position.Zero,
                        },
                        NewText = "using System;\n",
                    },
                ],
            },
        };

        await _manager.ApplyWorkspaceEditAsync(doc, edit);

        await mockClient.Received(1).ApplyWorkspaceEditAsync(edit, Arg.Any<CancellationToken>());
    }

    // ─── Restart ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RestartLanguageServerAsync_ShouldDisposeOldAndCreateNew()
    {
        var oldClient = Substitute.For<ILspClient, IAsyncDisposable>();
        oldClient.Language.Returns(Language.CSharp);
        oldClient.IsConnected.Returns(true);
        var newClient = CreateMockClient(Language.CSharp);

        var callCount = 0;
        _factory.CreateClientAsync(Language.CSharp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Task.FromResult<ILspClient?>(callCount == 1 ? oldClient : newClient);
            });

        var doc = CreateDocument("test.cs", Language.CSharp);
        var first = await _manager.GetClientAsync(doc);
        first.Should().BeSameAs(oldClient);

        await _manager.RestartLanguageServerAsync(Language.CSharp);

        // Old client should have been disposed
        await ((IAsyncDisposable)oldClient).Received(1).DisposeAsync();

        // Background task creates the new client; wait a moment then request
        await Task.Delay(100);
        var second = await _manager.GetClientAsync(doc);
        second.Should().BeSameAs(newClient);
    }

    [Fact]
    public async Task RestartLanguageServerAsync_ShouldReRegisterOpenDocuments()
    {
        var oldClient = Substitute.For<ILspClient, IAsyncDisposable>();
        oldClient.Language.Returns(Language.CSharp);
        oldClient.IsConnected.Returns(true);
        var newClient = CreateMockClient(Language.CSharp);

        var callCount = 0;
        _factory.CreateClientAsync(Language.CSharp, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Task.FromResult<ILspClient?>(callCount == 1 ? oldClient : newClient);
            });

        var doc = CreateDocument("test.cs", Language.CSharp);
        await _manager.GetClientAsync(doc);

        // Open a document (tracked internally)
        _manager.NotifyDocumentOpened(doc);

        await _manager.RestartLanguageServerAsync(Language.CSharp);

        // Wait for background client creation + re-registration
        await Task.Delay(200);

        // The new client should have received NotifyDocumentOpened
        newClient.Received(1).NotifyDocumentOpened(doc);
    }

    [Fact]
    public async Task RestartLanguageServerAsync_WithNoExistingClient_ShouldCreateNew()
    {
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        await _manager.RestartLanguageServerAsync(Language.CSharp);

        // Wait for background creation
        await Task.Delay(100);

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await _manager.GetClientAsync(doc);
        result.Should().BeSameAs(mockClient);
    }
}
