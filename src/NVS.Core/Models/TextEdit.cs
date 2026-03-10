namespace NVS.Core.Models;

public sealed record TextEdit
{
    public required Range Range { get; init; }
    public required string NewText { get; init; }
}
