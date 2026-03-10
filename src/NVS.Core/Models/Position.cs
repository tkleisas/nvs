namespace NVS.Core.Models;

public sealed record Position
{
    public required int Line { get; init; }
    public required int Column { get; init; }
    
    public static Position Zero => new() { Line = 0, Column = 0 };
    
    public bool IsBefore(Position other)
    {
        if (Line < other.Line) return true;
        if (Line > other.Line) return false;
        return Column < other.Column;
    }
    
    public bool IsAfter(Position other) => other.IsBefore(this);
}
