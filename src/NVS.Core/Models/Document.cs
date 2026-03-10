using System.Text;
using NVS.Core.Enums;

namespace NVS.Core.Models;

public sealed class Document
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public required string Name { get; set; }
    public Language Language { get; init; } = Language.Unknown;
    public DocumentState State { get; set; } = DocumentState.Unloaded;
    public string Content { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }
    public int Version { get; set; }
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public bool HasBom { get; init; }
    public string? LineEnding { get; init; } = Environment.NewLine;
}
