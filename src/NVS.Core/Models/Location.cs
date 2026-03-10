namespace NVS.Core.Models;

public sealed record Location
{
    public required string FilePath { get; init; }
    public required Range Range { get; init; }
}
