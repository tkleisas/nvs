using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

/// <summary>
/// Default factory that creates LspClient instances based on language server configurations
/// from the LanguageService and workspace settings.
/// </summary>
public sealed class LspClientFactory : ILspClientFactory
{
    private readonly ILanguageService _languageService;

    public LspClientFactory(ILanguageService languageService)
    {
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
    }

    public async Task<ILspClient?> CreateClientAsync(Language language, string rootPath, CancellationToken cancellationToken = default)
    {
        var serverName = _languageService.GetLanguageServer(language);
        if (serverName is null)
            return null;

        // Build a default config based on the server name.
        // In production, this could be overridden by workspace settings.
        var config = GetDefaultConfig(serverName);
        if (config is null)
            return null;

        var client = new LspClient(language, config, _languageService);
        await client.InitializeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private static LanguageServerConfig? GetDefaultConfig(string serverName) => serverName switch
    {
        "omnisharp" => new LanguageServerConfig
        {
            Command = "OmniSharp",
            Args = ["--languageserver"],
        },
        "clangd" => new LanguageServerConfig
        {
            Command = "clangd",
        },
        "typescript-language-server" => new LanguageServerConfig
        {
            Command = "typescript-language-server",
            Args = ["--stdio"],
        },
        "pyright" => new LanguageServerConfig
        {
            Command = "pyright-langserver",
            Args = ["--stdio"],
        },
        "rust-analyzer" => new LanguageServerConfig
        {
            Command = "rust-analyzer",
        },
        "gopls" => new LanguageServerConfig
        {
            Command = "gopls",
            Args = ["serve"],
        },
        _ => null,
    };
}
