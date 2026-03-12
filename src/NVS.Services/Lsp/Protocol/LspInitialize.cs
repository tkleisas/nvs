using System.Text.Json;
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
    public bool DidChange { get; init; }
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
    [JsonConverter(typeof(LspBoolOrObjectConverter<TextDocumentSyncOptions>))]
    public TextDocumentSyncOptions? TextDocumentSync { get; init; }
    public JsonElement? CompletionProvider { get; init; }
    public JsonElement? HoverProvider { get; init; }
    public JsonElement? DefinitionProvider { get; init; }
    public JsonElement? ReferencesProvider { get; init; }
    public JsonElement? DocumentSymbolProvider { get; init; }
    public JsonElement? DocumentFormattingProvider { get; init; }

    /// <summary>
    /// Checks if a capability (which may be bool or object) is enabled.
    /// </summary>
    public static bool IsEnabled(JsonElement? capability) =>
        capability?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Object => true,
            _ => false,
        };
}

public sealed record TextDocumentSyncOptions
{
    public bool OpenClose { get; init; }
    public TextDocumentSyncKind Change { get; init; }

    [JsonConverter(typeof(LspBoolOrObjectConverter<SaveOptions>))]
    public SaveOptions? Save { get; init; }
}

public sealed record SaveOptions
{
    public bool IncludeText { get; init; }
}

/// <summary>
/// Handles LSP union types where a field can be a boolean or a typed object.
/// true → new T(), false/null → null.
/// </summary>
public sealed class LspBoolOrObjectConverter<T> : JsonConverter<T?> where T : class, new()
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => new T(),
            JsonTokenType.False => null,
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt32(out var intVal) =>
                intVal switch
                {
                    // TextDocumentSyncKind can be sent as a number (0=None, 1=Full, 2=Incremental)
                    _ => CreateFromSyncKind(intVal),
                },
            JsonTokenType.StartObject => JsonSerializer.Deserialize<T>(ref reader, options),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for {typeof(T).Name}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value, options);
    }

    private static T? CreateFromSyncKind(int kind)
    {
        if (typeof(T) == typeof(TextDocumentSyncOptions))
        {
            var obj = new TextDocumentSyncOptions
            {
                Change = (TextDocumentSyncKind)kind,
                OpenClose = kind > 0,
            };
            return obj as T;
        }
        return new T();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2,
}
