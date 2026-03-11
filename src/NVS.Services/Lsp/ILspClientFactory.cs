using NVS.Core.Enums;
using NVS.Core.Interfaces;

namespace NVS.Services.Lsp;

/// <summary>
/// Factory for creating LSP client instances for specific languages.
/// Each language gets its own client connected to the appropriate language server.
/// </summary>
public interface ILspClientFactory
{
    /// <summary>
    /// Creates and initializes an LSP client for the given language.
    /// Returns null if no language server is configured for the language.
    /// </summary>
    Task<ILspClient?> CreateClientAsync(Language language, string rootPath, CancellationToken cancellationToken = default);
}
