namespace NVS.Core.Models;

/// <summary>
/// A project template backed by dotnet new (e.g. console, classlib, webapi).
/// </summary>
public sealed record ProjectTemplate
{
    public required string ShortName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public string? DefaultLanguage { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Frameworks { get; init; } = [];
}

/// <summary>
/// A file template for creating new source files (class, interface, record, etc.).
/// </summary>
public sealed record FileTemplate
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string DefaultFileName { get; init; }
    public required string Extension { get; init; }
    public required string ContentTemplate { get; init; }
    public string? Icon { get; init; }
}
