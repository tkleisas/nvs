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

    // ─── Roslyn Completion Routing ──────────────────────────────────────────

    [Fact]
    public async Task GetCompletionsAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);

        var roslynItems = new List<CompletionItem>
        {
            new() { Label = "Console", Kind = CompletionItemKind.Class },
        };
        roslynService.GetCompletionsAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(roslynItems);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("Console");
    }

    [Fact]
    public async Task GetCompletionsAsync_RoslynEmpty_ShouldFallbackToLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetCompletionsAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<CompletionItem>());

        var mockClient = CreateMockClient(Language.CSharp);
        var lspItems = new List<CompletionItem>
        {
            new() { Label = "Convert", Kind = CompletionItemKind.Class },
        };
        mockClient.GetCompletionsAsync(
            Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(lspItems);
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("Convert");
    }

    [Fact]
    public async Task GetCompletionsAsync_RoslynNotLoaded_ShouldUseLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(false);

        var mockClient = CreateMockClient(Language.CSharp);
        var lspItems = new List<CompletionItem> { new() { Label = "var" } };
        mockClient.GetCompletionsAsync(
            Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(lspItems);
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("var");
    }

    [Fact]
    public async Task GetCompletionsAsync_NonCSharp_ShouldAlwaysUseLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);

        var mockClient = CreateMockClient(Language.TypeScript);
        var lspItems = new List<CompletionItem> { new() { Label = "document" } };
        mockClient.GetCompletionsAsync(
            Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(lspItems);
        SetupFactory(Language.TypeScript, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.ts", Language.TypeScript);
        var result = await manager.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("document");

        // Roslyn should NOT have been called
        await roslynService.DidNotReceive().GetCompletionsAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyDocumentChanged_CSharp_ShouldForwardToRoslyn()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        var mockClient = CreateMockClient(Language.CSharp);
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        await manager.GetClientAsync(doc);

        manager.NotifyDocumentChanged(doc, "new content");

        roslynService.Received(1).UpdateDocumentContent(doc.FilePath!, "new content");
    }

    [Fact]
    public async Task NotifyDocumentChanged_NonCSharp_ShouldNotCallRoslyn()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        var mockClient = CreateMockClient(Language.Python);
        SetupFactory(Language.Python, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.py", Language.Python);
        await manager.GetClientAsync(doc);

        manager.NotifyDocumentChanged(doc, "new content");

        roslynService.DidNotReceive().UpdateDocumentContent(
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task GetCompletionsAsync_RoslynThrows_ShouldFallbackToLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetCompletionsAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CompletionItem>>(_ => throw new InvalidOperationException("Roslyn failure"));

        var mockClient = CreateMockClient(Language.CSharp);
        var lspItems = new List<CompletionItem> { new() { Label = "fallback" } };
        mockClient.GetCompletionsAsync(
            Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(lspItems);
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetCompletionsAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].Label.Should().Be("fallback");
    }

    // ─── Roslyn Hover Routing ───────────────────────────────────────────────

    [Fact]
    public async Task GetHoverAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetHoverAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new HoverInfo { Content = "Roslyn hover" });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetHoverAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.Content.Should().Be("Roslyn hover");
    }

    [Fact]
    public async Task GetHoverAsync_RoslynReturnsNull_ShouldFallbackToLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetHoverAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((HoverInfo?)null);

        var mockClient = CreateMockClient(Language.CSharp);
        mockClient.GetHoverAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new HoverInfo { Content = "LSP hover" });
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetHoverAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.Content.Should().Be("LSP hover");
    }

    // ─── Roslyn Definition Routing ──────────────────────────────────────────

    [Fact]
    public async Task GetDefinitionAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetDefinitionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Location { FilePath = @"C:\project\Def.cs", Range = Range.Empty });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetDefinitionAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(@"C:\project\Def.cs");
    }

    [Fact]
    public async Task GetDefinitionAsync_RoslynReturnsNull_ShouldFallbackToLsp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetDefinitionAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Location?)null);

        var mockClient = CreateMockClient(Language.CSharp);
        mockClient.GetDefinitionAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new Location { FilePath = @"C:\project\LspDef.cs", Range = Range.Empty });
        SetupFactory(Language.CSharp, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetDefinitionAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.FilePath.Should().Be(@"C:\project\LspDef.cs");
    }

    // ─── Roslyn References Routing ──────────────────────────────────────────

    [Fact]
    public async Task GetReferencesAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetReferencesAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Location> { new() { FilePath = "ref1.cs", Range = Range.Empty } });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetReferencesAsync(doc, Position.Zero);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("ref1.cs");
    }

    // ─── Roslyn Formatting Routing ──────────────────────────────────────────

    [Fact]
    public async Task FormatDocumentAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetFormattingEditsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<TextEdit> { new() { NewText = " ", Range = Range.Empty } });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.FormatDocumentAsync(doc);

        result.Should().HaveCount(1);
    }

    // ─── Roslyn Diagnostics Routing ─────────────────────────────────────────

    [Fact]
    public async Task GetDiagnosticsAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetDiagnosticsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<Diagnostic>
            {
                new() { Message = "CS0001", Severity = DiagnosticSeverity.Error, Range = Range.Empty, Source = "Roslyn" },
            });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetDiagnosticsAsync(doc);

        result.Should().HaveCount(1);
        result[0].Source.Should().Be("Roslyn");
    }

    // ─── Roslyn Document Symbols Routing ────────────────────────────────────

    [Fact]
    public async Task GetDocumentSymbolsAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<DocumentSymbol>
            {
                new() { Name = "MyClass", Kind = NVS.Core.Enums.SymbolKind.Class },
            });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetDocumentSymbolsAsync(doc);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("MyClass");
    }

    // ─── Roslyn Signature Help Routing ──────────────────────────────────────

    [Fact]
    public async Task GetSignatureHelpAsync_WithRoslyn_ShouldPreferRoslynForCSharp()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);
        roslynService.GetSignatureHelpAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SignatureHelp
            {
                Signatures = [new SignatureInformation { Label = "void Method(int x)" }],
                ActiveSignature = 0,
                ActiveParameter = 0,
            });

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.cs", Language.CSharp);
        var result = await manager.GetSignatureHelpAsync(doc, Position.Zero);

        result.Should().NotBeNull();
        result!.Signatures.Should().HaveCount(1);
        result.Signatures[0].Label.Should().Be("void Method(int x)");
    }

    // ─── Non-CSharp should always bypass Roslyn ─────────────────────────────

    [Fact]
    public async Task GetHoverAsync_NonCSharp_ShouldBypassRoslyn()
    {
        var roslynService = Substitute.For<IRoslynCompletionService>();
        roslynService.IsWorkspaceLoaded.Returns(true);

        var mockClient = CreateMockClient(Language.Python);
        mockClient.GetHoverAsync(Arg.Any<Document>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new HoverInfo { Content = "Python hover" });
        SetupFactory(Language.Python, mockClient);

        await using var manager = new LspSessionManager(_factory, roslynService);
        manager.SetRootPath(@"C:\project");

        var doc = CreateDocument("test.py", Language.Python);
        var result = await manager.GetHoverAsync(doc, Position.Zero);

        result!.Content.Should().Be("Python hover");
        await roslynService.DidNotReceive().GetHoverAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
