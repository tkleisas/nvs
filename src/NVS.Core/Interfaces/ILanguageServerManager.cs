using NVS.Core.Enums;
using NVS.Core.Models.Settings;

namespace NVS.Core.Interfaces;

public enum LanguageServerStatus
{
    NotInstalled,
    Installed,
    Unknown,
}

public interface ILanguageServerManager
{
    IReadOnlyList<LanguageServerDefinition> GetAvailableServers();
    LanguageServerDefinition? GetServerForLanguage(Language language);
    Task<LanguageServerStatus> CheckServerStatusAsync(string serverId, CancellationToken cancellationToken = default);
    Task<bool> InstallServerAsync(string serverId, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    string? FindServerBinary(string serverId);
}
