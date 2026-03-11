namespace NVS.Services.Lsp.Protocol;

// ─── Text Document Identifier ───────────────────────────────────────────────

public sealed record TextDocumentIdentifier
{
    public required string Uri { get; init; }
}

public sealed record VersionedTextDocumentIdentifier
{
    public required string Uri { get; init; }
    public int Version { get; init; }
}

public sealed record TextDocumentItem
{
    public required string Uri { get; init; }
    public required string LanguageId { get; init; }
    public int Version { get; init; }
    public required string Text { get; init; }
}

// ─── Position / Range (LSP wire format, 0-based) ───────────────────────────

public sealed record LspPosition
{
    public int Line { get; init; }
    public int Character { get; init; }
}

public sealed record LspRange
{
    public required LspPosition Start { get; init; }
    public required LspPosition End { get; init; }
}

public sealed record LspLocation
{
    public required string Uri { get; init; }
    public required LspRange Range { get; init; }
}

// ─── Text Document Position Params ──────────────────────────────────────────

public sealed record TextDocumentPositionParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public required LspPosition Position { get; init; }
}

// ─── Did Open / Change / Close / Save ───────────────────────────────────────

public sealed record DidOpenTextDocumentParams
{
    public required TextDocumentItem TextDocument { get; init; }
}

public sealed record DidChangeTextDocumentParams
{
    public required VersionedTextDocumentIdentifier TextDocument { get; init; }
    public required IReadOnlyList<TextDocumentContentChangeEvent> ContentChanges { get; init; }
}

public sealed record TextDocumentContentChangeEvent
{
    public string? Text { get; init; }
}

public sealed record DidCloseTextDocumentParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
}

public sealed record DidSaveTextDocumentParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public string? Text { get; init; }
}
