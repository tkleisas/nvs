using NVS.Core.Enums;

namespace NVS.Core.Models;

public sealed record Symbol
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public SymbolKind Kind { get; init; } = SymbolKind.Unknown;
    public required string FilePath { get; init; }
    public int LineStart { get; init; }
    public int LineEnd { get; init; }
    public int ColumnStart { get; init; }
    public int ColumnEnd { get; init; }
    public string? ContainerName { get; init; }
    public string? Documentation { get; init; }
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}
