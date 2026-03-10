namespace NVS.Core.Models;

public sealed record Range
{
    public required Position Start { get; init; }
    public required Position End { get; init; }
    
    public static Range Empty => new() { Start = Position.Zero, End = Position.Zero };
    
    public bool Contains(Position position)
    {
        return !position.IsBefore(Start) && !position.IsAfter(End);
    }
    
    public bool Overlaps(Range other)
    {
        return Start.IsBefore(other.End) && End.IsAfter(other.Start);
    }
}
