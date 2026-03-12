using System.Collections.Concurrent;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Lsp;

/// <summary>
/// Manages LSP client instances per language. Creates clients lazily on first
/// request for a given language and relays diagnostics events.
/// </summary>
public sealed class LspSessionManager : ILspSessionManager
{
    private readonly ILspClientFactory _factory;
    private readonly ConcurrentDictionary<Language, Task<ILspClient?>> _clients = new();
    private readonly ConcurrentDictionary<Language, ILspClient> _activeClients = new();
    private string? _rootPath;
    private bool _disposed;

    public event EventHandler<DocumentDiagnosticsEventArgs>? DiagnosticsChanged;

    public LspSessionManager(ILspClientFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Sets the workspace root path. Must be called before any LSP operations.
    /// </summary>
    public void SetRootPath(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
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

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(Document document, Position position, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(document, cancellationToken).ConfigureAwait(false);
        if (client is null)
            return [];

        return await client.GetCompletionsAsync(document, position, cancellationToken).ConfigureAwait(false);
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

    public void NotifyDocumentOpened(Document document)
    {
        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentOpened(document);
    }

    public void NotifyDocumentChanged(Document document, string content)
    {
        if (_activeClients.TryGetValue(document.Language, out var client))
            client.NotifyDocumentChanged(document, content);
    }

    public void NotifyDocumentClosed(Document document)
    {
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
        var client = await _factory.CreateClientAsync(language, rootPath, cancellationToken)
            .ConfigureAwait(false);

        if (client is null)
            return null;

        _activeClients[language] = client;
        client.DiagnosticsReceived += OnDiagnosticsReceived;
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
