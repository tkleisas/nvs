using System.Text.Json.Serialization;

namespace NVS.Services.Lsp.Protocol;

// ─── Completion ─────────────────────────────────────────────────────────────

public sealed record CompletionParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public required LspPosition Position { get; init; }
}

public sealed record CompletionList
{
    public bool IsIncomplete { get; init; }
    public IReadOnlyList<LspCompletionItem> Items { get; init; } = [];
}

public sealed record LspCompletionItem
{
    public required string Label { get; init; }
    public LspCompletionItemKind? Kind { get; init; }
    public string? Detail { get; init; }
    public MarkupContent? Documentation { get; init; }
    public string? InsertText { get; init; }
    public InsertTextFormat? InsertTextFormat { get; init; }
}

[JsonConverter(typeof(JsonNumberEnumConverter<LspCompletionItemKind>))]
public enum LspCompletionItemKind
{
    Text = 1,
    Method = 2,
    Function = 3,
    Constructor = 4,
    Field = 5,
    Variable = 6,
    Class = 7,
    Interface = 8,
    Module = 9,
    Property = 10,
    Unit = 11,
    Value = 12,
    Enum = 13,
    Keyword = 14,
    Snippet = 15,
    Color = 16,
    File = 17,
    Reference = 18,
    Folder = 19,
    EnumMember = 20,
    Constant = 21,
    Struct = 22,
    Event = 23,
    Operator = 24,
    TypeParameter = 25,
}

public enum InsertTextFormat
{
    PlainText = 1,
    Snippet = 2,
}

// ─── Hover ──────────────────────────────────────────────────────────────────

public sealed record HoverResult
{
    public required MarkupContent Contents { get; init; }
    public LspRange? Range { get; init; }
}

public sealed record MarkupContent
{
    public string Kind { get; init; } = "plaintext";
    public required string Value { get; init; }
}

// ─── Document Symbols ───────────────────────────────────────────────────────

public sealed record DocumentSymbolParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
}

public sealed record LspDocumentSymbol
{
    public required string Name { get; init; }
    public int Kind { get; init; }
    public required LspRange Range { get; init; }
    public required LspRange SelectionRange { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<LspDocumentSymbol>? Children { get; init; }
}

// ─── Formatting ─────────────────────────────────────────────────────────────

public sealed record DocumentFormattingParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public required FormattingOptions Options { get; init; }
}

public sealed record FormattingOptions
{
    public int TabSize { get; init; } = 4;
    public bool InsertSpaces { get; init; } = true;
}

public sealed record LspTextEdit
{
    public required LspRange Range { get; init; }
    public required string NewText { get; init; }
}

// ─── References ─────────────────────────────────────────────────────────────

public sealed record ReferenceParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public required LspPosition Position { get; init; }
    public required ReferenceContext Context { get; init; }
}

public sealed record ReferenceContext
{
    public bool IncludeDeclaration { get; init; } = true;
}

// ─── Diagnostics (server → client notification) ─────────────────────────────

// ─── Signature Help ─────────────────────────────────────────────────────────

public sealed record SignatureHelpParams
{
    public required TextDocumentIdentifier TextDocument { get; init; }
    public required LspPosition Position { get; init; }
    public SignatureHelpContext? Context { get; init; }
}

public sealed record SignatureHelpContext
{
    public required int TriggerKind { get; init; }
    public string? TriggerCharacter { get; init; }
    public bool IsRetrigger { get; init; }
}

public sealed record LspSignatureHelp
{
    public IReadOnlyList<LspSignatureInformation> Signatures { get; init; } = [];
    public int? ActiveSignature { get; init; }
    public int? ActiveParameter { get; init; }
}

public sealed record LspSignatureInformation
{
    public required string Label { get; init; }
    public MarkupContent? Documentation { get; init; }
    public IReadOnlyList<LspParameterInformation>? Parameters { get; init; }
}

public sealed record LspParameterInformation
{
    public required string Label { get; init; }
    public MarkupContent? Documentation { get; init; }
}

// ─── Diagnostics (server → client notification) ─────────────────────────────

public sealed record PublishDiagnosticsParams
{
    public required string Uri { get; init; }
    public IReadOnlyList<LspDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record LspDiagnostic
{
    public required LspRange Range { get; init; }
    public int? Severity { get; init; }
    public string? Code { get; init; }
    public string? Source { get; init; }
    public required string Message { get; init; }
}
