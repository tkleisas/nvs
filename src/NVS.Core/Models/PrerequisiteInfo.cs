using NVS.Core.Enums;

namespace NVS.Core.Models;

public sealed record PrerequisiteInfo
{
    public required Language Language { get; init; }
    public required string BinaryName { get; init; }
    public required string DisplayName { get; init; }
    public required string InstallHint { get; init; }
}
