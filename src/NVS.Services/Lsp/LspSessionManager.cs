using System.Collections.Concurrent;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using Serilog;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Lsp;

/// <summary>
/// Manages LSP client instances per language. Creates clients lazily on first
/// request for a given language and relays diagnostics events.
/// </summary>
public sealed class LspSessionManager : ILspSessionManager
{
    private static readonly ILogger Logger = Log.ForContext<LspSessionManager>();
    private readonly ILspClientFactory _factory;
    private readonly IRoslynCompletionService? _roslynCompletionService;
    private readonly ConcurrentDictionary<Language, Task<ILspClient?>> _clients = new();
    private readonly ConcurrentDictionary<Language, ILspClient> _activeClients = new();
    private readonly ConcurrentDictionary<string, Document> _openDocuments = new();
    private string? _rootPath;
    private bool _disposed;

    public event EventHandler<DocumentDiagnosticsEventArgs>? DiagnosticsChanged;

    public LspSessionManager(ILspClientFactory factory, IRoslynCompletionService? roslynCompletionService = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _roslynCompletionService = roslynCompletionService;
    }

    /// <summary>
    /// Sets the workspace root path. Must be called before any LSP operations.
    /// </summary>
    public void SetRootPath(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
    }

    /// <inheritdoc />
    public async Task RestartLanguageServerAsync(Language language, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Logger.Information("Restarting LSP server for {Language}", language);

        // Remove the cached task so a new client will be created
        _clients.TryRemove(language, out _);

        // Dispose the active client if it exists
        if (_activeClients.TryRemove(language, out var oldClient))
        {
            oldClient.DiagnosticsReceived -= OnDiagnosticsReceived;
            if (oldClient is IAsyncDisposable disposable)
            {
                try
                {
                    await disposable.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Error disposing old LSP client for {Language}", language);
                }
            }
            Logger.Information("Disposed old LSP client for {Language}", language);
        }

        // Create the new client in the background (may take a moment)
        if (_rootPath is not null)
        {
            var rootPath = _rootPath;
            _ = Task.Run(async () =>
            {
                try
                {
                    var clientTask = _clients.GetOrAdd(language, lang =>
                        CreateAndTrackClientAsync(lang, rootPath, cancellationToken));
                    var newClient = await clientTask.ConfigureAwait(false);

                    if (newClient is not null)
                    {
                        Logger.Information("New LSP client created for {Language}, re-registering open documents", language);

                        // Re-register all open documents for this language
                        foreach (var doc in _openDocuments.Values)
                        {
                            if (doc.Language == language)
                            {
                                newClient.NotifyDocumentOpened(doc);
                                Logger.Debug("Re-registered document {Path} with new LSP client", doc.Path);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to create new LSP client for {Language}", language);
                    _clients.TryRemove(language, out _);
                }
            }, cancellationToken);
        }
    }

    public async Task<ILspClient?> GetClientAsync(Document document, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (document.Language == Language.Unknown)
            return null;

        var rootPath = _rootPath ?? GetRootPathFromDocument(document);

        var clientTask = _clients.GetOrAdd(document.Language, lang =>
            CreateAndTrackClientAsync(lang, rootPath, cancellationToken));

        try
        {
            return await clientTask.ConfigureAwait(false);
        }
        catch
        {
            // Remove the faulted task so the next call can retry
            _clients.TryRemove(new KeyValuePair<Language, Task<ILspClient?>>(document.Language, clientTask));
            throw;
        }
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, string? triggerChar = null, CancellationToken cancellationToken = default)
    {
        // For C# files, prefer Roslyn completions when workspace is loaded
        if (document.Language == Language.CSharp
            && _roslynCompletionService is { IsWorkspaceLoaded: true }
            && document.FilePath is not null)
        {
            try
            {
                var results = await _roslynCompletionService.GetCompletionsAsync(
                    document.FilePath, position.Line, position.Column, cancellationToken).ConfigureAwait(false);
                if (results.Count > 0)
                    return results;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Roslyn completions failed, falling back to LSP");
            }
        }

        // Fall back to LSP
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return [];

        return await client.GetCompletionsAsync(document, position, triggerChar, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SignatureHelp?> GetSignatureHelpAsync(Document document, Position position, string? triggerChar = null, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return null;

        return await client.GetSignatureHelpAsync(document, position, triggerChar, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Location?> GetDefinitionAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return null;

        return await client.GetDefinitionAsync(document, position, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Location>> GetReferencesAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return [];

        return await client.GetReferencesAsync(document, position, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HoverInfo?> GetHoverAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return null;

        return await client.GetHoverAsync(document, position, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TextEdit>> FormatDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return [];

        return await client.GetFormattingEditsAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(Document document, Range range, IReadOnlyList<Diagnostic> diagnostics, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return [];

        return await client.GetCodeActionsAsync(document, range, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyWorkspaceEditAsync(Document document, WorkspaceEdit edit, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return;

        await client.ApplyWorkspaceEditAsync(edit, cancellationToken).ConfigureAwait(false);
    }

    public void NotifyDocumentOpened(Document document)
    {
        var key = document.FilePath ?? document.Path;
        _openDocuments[key] = document;

        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentOpened(document);
    }

    public void NotifyDocumentChanged(Document document, string content)
    {
        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentChanged(document, content);

        // Keep Roslyn workspace in sync for C# files
        if (document.Language == Language.CSharp && document.FilePath is not null)
            _roslynCompletionService?.UpdateDocumentContent(document.FilePath, content);
    }

    public async Task NotifyDocumentChangedAsync(Document document, string content)
    {
        if (_activeClients.TryGetValue(document.Language, out var client))
            await client.NotifyDocumentChangedAsync(document, content).ConfigureAwait(false);

        if (document.Language == Language.CSharp && document.FilePath is not null)
            _roslynCompletionService?.UpdateDocumentContent(document.FilePath, content);
    }

    public void NotifyDocumentClosed(Document document)
    {
        var key = document.FilePath ?? document.Path;
        _openDocuments.TryRemove(key, out _);

        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentClosed(document);
    }

    public void NotifyDocumentSaved(Document document)
    {
        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentSaved(document);
    }

    private async Task<ILspClient?> CreateAndTrackClientAsync(Language language, string rootPath, CancellationToken cancellationToken)
    {
        Logger.Information("Creating LSP client for {Language} with root {RootPath}", language, rootPath);

        var client = await _factory.CreateClientAsync(language, rootPath, cancellationToken)
            .ConfigureAwait(false);

        if (client is null)
        {
            Logger.Warning("No LSP client available for {Language}", language);
            return null;
        }

        _activeClients[language] = client;
        client.DiagnosticsReceived += OnDiagnosticsReceived;
        Logger.Information("LSP client ready for {Language}", language);
        return client;
    }

    private void OnDiagnosticsReceived(object? sender, DocumentDiagnosticsEventArgs args)
    {
        DiagnosticsChanged?.Invoke(this, args);
    }

    private static string GetRootPathFromDocument(Document document)
    {
        var filePath = document.FilePath ?? document.Path;
        return Path.GetDirectoryName(filePath) ?? ".";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var (_, client) in _activeClients)
        {
            client.DiagnosticsReceived -= OnDiagnosticsReceived;
            if (client is IAsyncDisposable disposable)
                await disposable.DisposeAsync().ConfigureAwait(false);
        }

        _activeClients.Clear();
        _clients.Clear();
    }
}
