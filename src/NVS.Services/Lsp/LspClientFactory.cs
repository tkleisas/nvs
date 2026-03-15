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
        // Check if user has a preferred server for this language
        var definition = ResolveServerDefinition(language);
        if (definition is null)
            return null;

        // Check user config for overrides or disabled state
        var userConfig = _settingsService.AppSettings.LanguageServers
            .GetValueOrDefault(definition.Id);

        if (userConfig is { Enabled: false })
            return null;

        var config = BuildConfig(definition, userConfig, rootPath);

        var client = new LspClient(language, config, _languageService);
        await client.InitializeAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return client;
    }

    private LanguageServerDefinition? ResolveServerDefinition(Language language)
    {
        var languageName = language.ToString();
        var preferred = _settingsService.AppSettings.PreferredLanguageServers;

        if (preferred.TryGetValue(languageName, out var preferredId))
        {
            var preferredDef = LanguageServerRegistry.GetById(preferredId);
            if (preferredDef is not null)
                return preferredDef;
        }

        return LanguageServerRegistry.GetForLanguage(language);
    }

    private static LanguageServerConfig BuildConfig(
        LanguageServerDefinition definition,
        LanguageServerUserConfig? userConfig,
        string rootPath)
    {
        var command = userConfig?.CustomCommand ?? definition.BinaryName;
        var baseArgs = userConfig?.CustomArgs is { Count: > 0 }
            ? userConfig.CustomArgs
            : definition.DefaultArgs;

        // Build final args list, prepending solution path if required
        var args = baseArgs;
        if (definition.RequiresSolutionArg && !string.IsNullOrEmpty(definition.SolutionArgPrefix))
        {
            var argsList = new List<string> { definition.SolutionArgPrefix, rootPath };
            argsList.AddRange(baseArgs);
            args = argsList;
        }

        // Resolve the binary name to a full path so that language servers
        // installed in well-known directories (e.g. ~/.dotnet/tools on Linux)
        // are found even when those directories aren't in $PATH.
        var resolvedCommand = LanguageServerManager.FindBinaryOnPath(command)
            ?? FindInNvsTools(definition.Id, command)
            ?? command;

        return new LanguageServerConfig
        {
            Command = resolvedCommand,
            Args = args,
        };
    }

    private static string? FindInNvsTools(string serverId, string binaryName)
    {
        var toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NVS", "tools", serverId);

        if (!Directory.Exists(toolsDir))
            return null;

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var ext in extensions)
        {
            var fullPath = Path.Combine(toolsDir, binaryName + ext);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
