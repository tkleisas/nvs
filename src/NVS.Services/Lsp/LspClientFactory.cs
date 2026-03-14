using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

/// <summary>
/// Factory that creates LspClient instances using the language server registry
/// and optional user configuration overrides from settings.
/// </summary>
public sealed class LspClientFactory : ILspClientFactory
{
    private readonly ILanguageService _languageService;
    private readonly ISettingsService _settingsService;

    public LspClientFactory(ILanguageService languageService, ISettingsService settingsService)
    {
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public async Task<ILspClient?> CreateClientAsync(Language language, string rootPath, CancellationToken cancellationToken = default)
    {
        var definition = LanguageServerRegistry.GetForLanguage(language);
        if (definition is null)
            return null;

        // Check user config for overrides or disabled state
        var userConfig = _settingsService.AppSettings.LanguageServers
            .GetValueOrDefault(definition.Id);

        if (userConfig is { Enabled: false })
            return null;

        var config = BuildConfig(definition, userConfig);

        var client = new LspClient(language, config, _languageService);
        await client.InitializeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private static LanguageServerConfig BuildConfig(
        LanguageServerDefinition definition,
        LanguageServerUserConfig? userConfig)
    {
        var command = userConfig?.CustomCommand ?? definition.BinaryName;
        var args = userConfig?.CustomArgs is { Count: > 0 }
            ? userConfig.CustomArgs
            : definition.DefaultArgs;

        // Resolve the binary name to a full path so that language servers
        // installed in well-known directories (e.g. ~/.dotnet/tools on Linux)
        // are found even when those directories aren't in $PATH.
        var resolvedCommand = LanguageServerManager.FindBinaryOnPath(command) ?? command;

        return new LanguageServerConfig
        {
            Command = resolvedCommand,
            Args = args,
        };
    }
}
