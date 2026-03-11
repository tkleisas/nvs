using System.Text.Json.Serialization;

namespace NVS.Services.Lsp.Protocol;

// ─── Initialize ─────────────────────────────────────────────────────────────

public sealed record InitializeParams
{
    public int ProcessId { get; init; }
    public required string RootUri { get; init; }
    public required ClientCapabilities Capabilities { get; init; }
}

public sealed record ClientCapabilities
{
    public TextDocumentClientCapabilities? TextDocument { get; init; }
}

public sealed record TextDocumentClientCapabilities
{
    public CompletionClientCapabilities? Completion { get; init; }
    public HoverClientCapabilities? Hover { get; init; }
    public DefinitionClientCapabilities? Definition { get; init; }
    public ReferencesClientCapabilities? References { get; init; }
    public DocumentSymbolClientCapabilities? DocumentSymbol { get; init; }
    public FormattingClientCapabilities? Formatting { get; init; }
    public PublishDiagnosticsClientCapabilities? PublishDiagnostics { get; init; }
    public SynchronizationClientCapabilities? Synchronization { get; init; }
}

public sealed record CompletionClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record HoverClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record DefinitionClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record ReferencesClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record DocumentSymbolClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record FormattingClientCapabilities
{
    public bool DynamicRegistration { get; init; }
}

public sealed record PublishDiagnosticsClientCapabilities
{
    public bool RelatedInformation { get; init; }
}

public sealed record SynchronizationClientCapabilities
{
    public bool DidSave { get; init; }
    public bool WillSave { get; init; }
}

// ─── Initialize Result ──────────────────────────────────────────────────────

public sealed record InitializeResult
{
    public required ServerCapabilities Capabilities { get; init; }
    public ServerInfo? ServerInfo { get; init; }
}

public sealed record ServerInfo
{
    public required string Name { get; init; }
    public string? Version { get; init; }
}

public sealed record ServerCapabilities
{
    public TextDocumentSyncOptions? TextDocumentSync { get; init; }
    public bool? CompletionProvider { get; init; }
    public bool? HoverProvider { get; init; }
    public bool? DefinitionProvider { get; init; }
    public bool? ReferencesProvider { get; init; }
    public bool? DocumentSymbolProvider { get; init; }
    public bool? DocumentFormattingProvider { get; init; }
}

public sealed record TextDocumentSyncOptions
{
    public bool OpenClose { get; init; }
    public TextDocumentSyncKind Change { get; init; }
    public bool Save { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2,
}
