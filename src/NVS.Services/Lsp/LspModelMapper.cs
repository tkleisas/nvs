using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Lsp.Protocol;
using Range = NVS.Core.Models.Range;

namespace NVS.Services.Lsp;

/// <summary>
/// Maps between LSP protocol wire types and NVS.Core model types.
/// </summary>
internal static class LspModelMapper
{
    // ─── To LSP ─────────────────────────────────────────────────────────────

    public static LspPosition ToLspPosition(Position position) => new()
    {
        Line = position.Line,
        Character = position.Column,
    };

    public static LspRange ToLspRange(Range range) => new()
    {
        Start = ToLspPosition(range.Start),
        End = ToLspPosition(range.End),
    };

    public static string ToUri(string filePath) =>
        new Uri(filePath).AbsoluteUri;

    public static TextDocumentIdentifier ToTextDocumentIdentifier(Document document) => new()
    {
        Uri = ToUri(document.FilePath ?? document.Path),
    };

    public static VersionedTextDocumentIdentifier ToVersionedTextDocumentIdentifier(Document document) => new()
    {
        Uri = ToUri(document.FilePath ?? document.Path),
        Version = document.Version,
    };

    public static TextDocumentItem ToTextDocumentItem(Document document, string languageId) => new()
    {
        Uri = ToUri(document.FilePath ?? document.Path),
        LanguageId = languageId,
        Version = document.Version,
        Text = document.Content,
    };

    // ─── From LSP ───────────────────────────────────────────────────────────

    public static Position FromLspPosition(LspPosition position) => new()
    {
        Line = position.Line,
        Column = position.Character,
    };

    public static Range FromLspRange(LspRange range) => new()
    {
        Start = FromLspPosition(range.Start),
        End = FromLspPosition(range.End),
    };

    public static string FromUri(string uri)
    {
        try
        {
            return new Uri(uri).LocalPath;
        }
        catch
        {
            return uri;
        }
    }

    public static Location FromLspLocation(LspLocation location) => new()
    {
        FilePath = FromUri(location.Uri),
        Range = FromLspRange(location.Range),
    };

    public static CompletionItem FromLspCompletionItem(LspCompletionItem item) => new()
    {
        Label = item.Label,
        Detail = item.Detail,
        Documentation = item.Documentation?.Value,
        Kind = MapCompletionItemKind(item.Kind),
        InsertText = item.InsertText,
        IsSnippet = item.InsertTextFormat == InsertTextFormat.Snippet,
    };

    public static HoverInfo FromHoverResult(HoverResult result) => new()
    {
        Content = result.Contents.Value,
        Range = result.Range is not null ? FromLspRange(result.Range) : null,
    };

    public static DocumentSymbol FromLspDocumentSymbol(LspDocumentSymbol symbol) => new()
    {
        Name = symbol.Name,
        Kind = (SymbolKind)symbol.Kind,
        Range = FromLspRange(symbol.Range),
        SelectionRange = FromLspRange(symbol.SelectionRange),
        Detail = symbol.Detail,
        Children = symbol.Children?.Select(FromLspDocumentSymbol).ToList() ?? [],
    };

    public static TextEdit FromLspTextEdit(LspTextEdit edit) => new()
    {
        Range = FromLspRange(edit.Range),
        NewText = edit.NewText,
    };

    public static Diagnostic FromLspDiagnostic(LspDiagnostic diagnostic) => new()
    {
        Message = diagnostic.Message,
        Severity = MapDiagnosticSeverity(diagnostic.Severity),
        Range = FromLspRange(diagnostic.Range),
        Source = diagnostic.Source,
        Code = diagnostic.Code,
    };

    public static SignatureHelp FromSignatureHelp(LspSignatureHelp lsp) => new()
    {
        Signatures = lsp.Signatures.Select(FromSignatureInformation).ToList(),
        ActiveSignature = lsp.ActiveSignature ?? 0,
        ActiveParameter = lsp.ActiveParameter ?? 0,
    };

    private static SignatureInformation FromSignatureInformation(LspSignatureInformation lsp) => new()
    {
        Label = lsp.Label,
        Documentation = lsp.Documentation?.Value,
        Parameters = lsp.Parameters?.Select(p => new ParameterInformation
        {
            Label = p.Label,
            Documentation = p.Documentation?.Value,
        }).ToList() ?? [],
    };

    // ─── Kind Mapping ───────────────────────────────────────────────────────

    private static CompletionItemKind MapCompletionItemKind(LspCompletionItemKind? kind) => kind switch
    {
        LspCompletionItemKind.Method => CompletionItemKind.Method,
        LspCompletionItemKind.Function => CompletionItemKind.Function,
        LspCompletionItemKind.Constructor => CompletionItemKind.Constructor,
        LspCompletionItemKind.Field => CompletionItemKind.Field,
        LspCompletionItemKind.Variable => CompletionItemKind.Variable,
        LspCompletionItemKind.Class => CompletionItemKind.Class,
        LspCompletionItemKind.Interface => CompletionItemKind.Interface,
        LspCompletionItemKind.Module => CompletionItemKind.Module,
        LspCompletionItemKind.Property => CompletionItemKind.Property,
        LspCompletionItemKind.Keyword => CompletionItemKind.Keyword,
        LspCompletionItemKind.Snippet => CompletionItemKind.Snippet,
        _ => CompletionItemKind.Text,
    };

    private static DiagnosticSeverity MapDiagnosticSeverity(int? severity) => severity switch
    {
        1 => DiagnosticSeverity.Error,
        2 => DiagnosticSeverity.Warning,
        3 => DiagnosticSeverity.Information,
        4 => DiagnosticSeverity.Hint,
        _ => DiagnosticSeverity.Information,
    };
}
