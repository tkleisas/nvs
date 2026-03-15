using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using Serilog;
using CompletionItem = NVS.Core.Interfaces.CompletionItem;
using CompletionItemKind = NVS.Core.Interfaces.CompletionItemKind;
using Document = Microsoft.CodeAnalysis.Document;
using NvsDiagnostic = NVS.Core.Interfaces.Diagnostic;
using NvsDiagnosticSeverity = NVS.Core.Interfaces.DiagnosticSeverity;
using NvsDocumentSymbol = NVS.Core.Interfaces.DocumentSymbol;
using NvsLocation = NVS.Core.Models.Location;
using NvsRange = NVS.Core.Models.Range;
using NvsSymbolKind = NVS.Core.Enums.SymbolKind;
using RoslynSolution = Microsoft.CodeAnalysis.Solution;

namespace NVS.Services.Roslyn;

/// <summary>
/// Provides C# language features using Roslyn's MSBuildWorkspace.
/// Handles completions, hover, go-to-definition, references, diagnostics,
/// document symbols, signature help, and formatting.
/// </summary>
public sealed class RoslynCompletionService : IRoslynCompletionService
{
    private static readonly ILogger Logger = Log.ForContext<RoslynCompletionService>();

    private static readonly object LocatorLock = new();
    private static bool _locatorRegistered;

    private MSBuildWorkspace? _workspace;
    private RoslynSolution? _solution;
    private bool _disposed;

    private readonly ConcurrentDictionary<string, DocumentId> _documentMap = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWorkspaceLoaded => _solution is not null;

    // ─── Workspace Loading ──────────────────────────────────────────────────

    public async Task LoadWorkspaceAsync(string solutionOrProjectPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureMSBuildRegistered();
        UnloadWorkspace();

        Logger.Information("[Roslyn] Loading workspace from {Path}", solutionOrProjectPath);

        _workspace = CreateWorkspace();

        _workspace.WorkspaceFailed += (_, args) =>
            Logger.Warning("[Roslyn] Workspace diagnostic: {Kind} — {Message}", args.Diagnostic.Kind, args.Diagnostic.Message);

        var ext = Path.GetExtension(solutionOrProjectPath);
        if (ext is ".slnx")
        {
            await LoadSlnxAsync(solutionOrProjectPath, cancellationToken).ConfigureAwait(false);
        }
        else if (ext is ".sln")
        {
            _solution = await _workspace.OpenSolutionAsync(solutionOrProjectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        else if (ext is ".csproj")
        {
            var project = await _workspace.OpenProjectAsync(solutionOrProjectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _solution = project.Solution;
        }
        else
        {
            Logger.Warning("[Roslyn] Unsupported workspace file: {Path}", solutionOrProjectPath);
            return;
        }

        RebuildDocumentMap();

        var projectCount = _solution?.Projects.Count() ?? 0;
        var docCount = _documentMap.Count;
        Logger.Information("[Roslyn] Workspace loaded: {Projects} project(s), {Documents} document(s)", projectCount, docCount);
    }

    private async Task LoadSlnxAsync(string slnxPath, CancellationToken cancellationToken)
    {
        var solutionDir = Path.GetDirectoryName(slnxPath)!;
        var projectPaths = ParseSlnxProjectPaths(slnxPath, solutionDir);

        if (projectPaths.Count == 0)
        {
            Logger.Warning("[Roslyn] No projects found in .slnx: {Path}", slnxPath);
            return;
        }

        Logger.Information("[Roslyn] Parsed .slnx: {Count} project(s)", projectPaths.Count);

        foreach (var projectPath in projectPaths)
        {
            try
            {
                var project = await _workspace!.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _solution = project.Solution;
                Logger.Debug("[Roslyn] Loaded project: {Path}", projectPath);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[Roslyn] Failed to load project: {Path}", projectPath);
            }
        }
    }

    private static List<string> ParseSlnxProjectPaths(string slnxPath, string solutionDir)
    {
        var paths = new List<string>();
        try
        {
            var doc = System.Xml.Linq.XDocument.Load(slnxPath);
            foreach (var el in doc.Descendants("Project"))
            {
                var pathAttr = el.Attribute("Path")?.Value;
                if (pathAttr is not null)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(solutionDir, pathAttr));
                    if (File.Exists(fullPath))
                        paths.Add(fullPath);
                    else
                        Log.Warning("[Roslyn] Project not found: {Path}", fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Roslyn] Failed to parse .slnx: {Path}", slnxPath);
        }
        return paths;
    }

    // ─── Completions ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        string filePath, int line, int column, string? triggerChar = null, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return [];

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return [];

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);
        if (position < 0 || position > text.Length) return [];

        var trigger = triggerChar is { Length: > 0 }
            ? CompletionTrigger.CreateInsertionTrigger(triggerChar[0])
            : CompletionTrigger.Invoke;

        var completionList = await completionService.GetCompletionsAsync(
            document, position, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (completionList is null) return [];

        var results = new List<CompletionItem>(completionList.ItemsList.Count);
        foreach (var item in completionList.ItemsList)
            results.Add(MapCompletionItem(item));

        return results;
    }

    // ─── Hover ──────────────────────────────────────────────────────────────

    public async Task<HoverInfo?> GetHoverAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);
        if (position < 0 || position > text.Length) return null;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var symbolInfo = semanticModel.GetSymbolInfo(
            (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!
                .FindToken(position).Parent!, cancellationToken);

        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is null) return null;

        var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var kind = symbol.Kind.ToString();
        var doc = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        var summary = ExtractXmlSummary(doc);

        var content = string.IsNullOrEmpty(summary)
            ? $"```csharp\n({kind}) {display}\n```"
            : $"```csharp\n({kind}) {display}\n```\n\n{summary}";

        var token = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!
            .FindToken(position);
        var span = token.Span;
        var startLine = text.Lines.GetLineFromPosition(span.Start);
        var endLine = text.Lines.GetLineFromPosition(span.End);

        return new HoverInfo
        {
            Content = content,
            Range = new NvsRange
            {
                Start = new Position { Line = startLine.LineNumber, Column = span.Start - startLine.Start },
                End = new Position { Line = endLine.LineNumber, Column = span.End - endLine.Start },
            },
        };
    }

    // ─── Go to Definition ───────────────────────────────────────────────────

    public async Task<NvsLocation?> GetDefinitionAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);
        if (position < 0 || position > text.Length) return null;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return null;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var symbolInfo = semanticModel.GetSymbolInfo(root!.FindToken(position).Parent!, cancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (symbol is null) return null;

        // Try to find the source definition
        var sourceSymbol = await SymbolFinder.FindSourceDefinitionAsync(symbol, _solution!, cancellationToken)
            .ConfigureAwait(false);
        symbol = sourceSymbol ?? symbol;

        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (loc is null) return null;

        var locText = loc.SourceTree is not null
            ? await loc.SourceTree.GetTextAsync(cancellationToken).ConfigureAwait(false)
            : null;
        if (locText is null) return null;

        var locSpan = loc.GetLineSpan();
        return new NvsLocation
        {
            FilePath = loc.SourceTree!.FilePath,
            Range = new NvsRange
            {
                Start = new Position { Line = locSpan.StartLinePosition.Line, Column = locSpan.StartLinePosition.Character },
                End = new Position { Line = locSpan.EndLinePosition.Line, Column = locSpan.EndLinePosition.Character },
            },
        };
    }

    // ─── Find References ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<NvsLocation>> GetReferencesAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return [];

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);
        if (position < 0 || position > text.Length) return [];

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return [];

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var symbolInfo = semanticModel.GetSymbolInfo(root!.FindToken(position).Parent!, cancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is null) return [];

        var references = await SymbolFinder.FindReferencesAsync(symbol, _solution!, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<NvsLocation>();
        foreach (var refSymbol in references)
        {
            foreach (var refLoc in refSymbol.Locations)
            {
                var span = refLoc.Location.GetLineSpan();
                results.Add(new NvsLocation
                {
                    FilePath = refLoc.Document.FilePath ?? "",
                    Range = new NvsRange
                    {
                        Start = new Position { Line = span.StartLinePosition.Line, Column = span.StartLinePosition.Character },
                        End = new Position { Line = span.EndLinePosition.Line, Column = span.EndLinePosition.Character },
                    },
                });
            }
        }
        return results;
    }

    // ─── Document Symbols ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<NvsDocumentSymbol>> GetDocumentSymbolsAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return [];

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return [];

        var results = new List<NvsDocumentSymbol>();
        CollectSymbols(root, text, results);
        return results;
    }

    private static void CollectSymbols(SyntaxNode node, SourceText text, List<NvsDocumentSymbol> results)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case NamespaceDeclarationSyntax ns:
                    results.Add(MakeSymbol(ns.Name.ToString(), NvsSymbolKind.Namespace, ns.Span, ns.Name.Span, text,
                        CollectChildSymbols(ns, text)));
                    break;
                case FileScopedNamespaceDeclarationSyntax ns:
                    results.Add(MakeSymbol(ns.Name.ToString(), NvsSymbolKind.Namespace, ns.Span, ns.Name.Span, text,
                        CollectChildSymbols(ns, text)));
                    break;
                case ClassDeclarationSyntax cls:
                    results.Add(MakeSymbol(cls.Identifier.Text, NvsSymbolKind.Class, cls.Span, cls.Identifier.Span, text,
                        CollectChildSymbols(cls, text)));
                    break;
                case StructDeclarationSyntax str:
                    results.Add(MakeSymbol(str.Identifier.Text, NvsSymbolKind.Struct, str.Span, str.Identifier.Span, text,
                        CollectChildSymbols(str, text)));
                    break;
                case InterfaceDeclarationSyntax iface:
                    results.Add(MakeSymbol(iface.Identifier.Text, NvsSymbolKind.Interface, iface.Span, iface.Identifier.Span, text,
                        CollectChildSymbols(iface, text)));
                    break;
                case EnumDeclarationSyntax en:
                    results.Add(MakeSymbol(en.Identifier.Text, NvsSymbolKind.Enum, en.Span, en.Identifier.Span, text,
                        CollectChildSymbols(en, text)));
                    break;
                case RecordDeclarationSyntax rec:
                    results.Add(MakeSymbol(rec.Identifier.Text, NvsSymbolKind.Class, rec.Span, rec.Identifier.Span, text,
                        CollectChildSymbols(rec, text)));
                    break;
                case MethodDeclarationSyntax m:
                    results.Add(MakeSymbol(m.Identifier.Text, NvsSymbolKind.Method, m.Span, m.Identifier.Span, text));
                    break;
                case ConstructorDeclarationSyntax c:
                    results.Add(MakeSymbol(c.Identifier.Text, NvsSymbolKind.Constructor, c.Span, c.Identifier.Span, text));
                    break;
                case PropertyDeclarationSyntax p:
                    results.Add(MakeSymbol(p.Identifier.Text, NvsSymbolKind.Property, p.Span, p.Identifier.Span, text));
                    break;
                case FieldDeclarationSyntax f:
                    foreach (var variable in f.Declaration.Variables)
                        results.Add(MakeSymbol(variable.Identifier.Text, NvsSymbolKind.Field, f.Span, variable.Identifier.Span, text));
                    break;
                case EventFieldDeclarationSyntax e:
                    foreach (var variable in e.Declaration.Variables)
                        results.Add(MakeSymbol(variable.Identifier.Text, NvsSymbolKind.Event, e.Span, variable.Identifier.Span, text));
                    break;
                case EnumMemberDeclarationSyntax em:
                    results.Add(MakeSymbol(em.Identifier.Text, NvsSymbolKind.EnumMember, em.Span, em.Identifier.Span, text));
                    break;
                default:
                    CollectSymbols(child, text, results);
                    break;
            }
        }
    }

    private static List<NvsDocumentSymbol> CollectChildSymbols(SyntaxNode node, SourceText text)
    {
        var children = new List<NvsDocumentSymbol>();
        CollectSymbols(node, text, children);
        return children;
    }

    private static NvsDocumentSymbol MakeSymbol(string name, NvsSymbolKind kind, TextSpan fullSpan, TextSpan nameSpan,
        SourceText text, IReadOnlyList<NvsDocumentSymbol>? children = null)
    {
        return new NvsDocumentSymbol
        {
            Name = name,
            Kind = kind,
            Range = SpanToRange(fullSpan, text),
            SelectionRange = SpanToRange(nameSpan, text),
            Children = children ?? [],
        };
    }

    // ─── Diagnostics ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<NvsDiagnostic>> GetDiagnosticsAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return [];

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return [];

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);

        var results = new List<NvsDiagnostic>();
        foreach (var diag in diagnostics)
        {
            if (diag.Location.Kind != LocationKind.SourceFile) continue;

            var span = diag.Location.GetLineSpan();
            results.Add(new NvsDiagnostic
            {
                Message = diag.GetMessage(),
                Severity = MapSeverity(diag.Severity),
                Range = new NvsRange
                {
                    Start = new Position { Line = span.StartLinePosition.Line, Column = span.StartLinePosition.Character },
                    End = new Position { Line = span.EndLinePosition.Line, Column = span.EndLinePosition.Character },
                },
                Source = "Roslyn",
                Code = diag.Id,
            });
        }
        return results;
    }

    // ─── Signature Help ─────────────────────────────────────────────────────

    public async Task<Core.Interfaces.SignatureHelp?> GetSignatureHelpAsync(
        string filePath, int line, int column, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return null;

        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var position = GetOffset(text, line, column);
        if (position < 0 || position > text.Length) return null;

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return null;

        // Walk up from the position to find an invocation expression
        var node = root.FindToken(position).Parent;
        InvocationExpressionSyntax? invocation = null;
        ObjectCreationExpressionSyntax? objectCreation = null;

        while (node is not null)
        {
            if (node is InvocationExpressionSyntax inv)
            {
                invocation = inv;
                break;
            }
            if (node is ObjectCreationExpressionSyntax oc)
            {
                objectCreation = oc;
                break;
            }
            node = node.Parent;
        }

        if (invocation is null && objectCreation is null) return null;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return null;

        // Determine active parameter index from argument list
        var argList = invocation?.ArgumentList ?? objectCreation?.ArgumentList;
        int activeParam = 0;
        if (argList is not null)
        {
            foreach (var arg in argList.Arguments)
            {
                if (arg.Span.End <= position)
                    activeParam++;
            }
        }

        IMethodSymbol[] methods;
        if (invocation is not null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
            var list = new List<IMethodSymbol>();
            if (symbolInfo.Symbol is IMethodSymbol m) list.Add(m);
            list.AddRange(symbolInfo.CandidateSymbols.OfType<IMethodSymbol>());
            methods = list.ToArray();
        }
        else
        {
            var symbolInfo = semanticModel.GetSymbolInfo(objectCreation!, cancellationToken);
            var list = new List<IMethodSymbol>();
            if (symbolInfo.Symbol is IMethodSymbol m) list.Add(m);
            list.AddRange(symbolInfo.CandidateSymbols.OfType<IMethodSymbol>());
            methods = list.ToArray();
        }

        if (methods.Length == 0) return null;

        var signatures = new List<SignatureInformation>();
        foreach (var method in methods)
        {
            var paramLabels = method.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}");
            var label = $"{method.Name}({string.Join(", ", paramLabels)})";

            var parameters = method.Parameters.Select(p => new ParameterInformation
            {
                Label = $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}",
                Documentation = ExtractXmlSummary(p.GetDocumentationCommentXml(cancellationToken: cancellationToken)),
            }).ToList();

            var doc = method.GetDocumentationCommentXml(cancellationToken: cancellationToken);
            signatures.Add(new SignatureInformation
            {
                Label = label,
                Documentation = ExtractXmlSummary(doc),
                Parameters = parameters,
            });
        }

        return new Core.Interfaces.SignatureHelp
        {
            Signatures = signatures,
            ActiveSignature = 0,
            ActiveParameter = activeParam,
        };
    }

    // ─── Formatting ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TextEdit>> GetFormattingEditsAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var document = FindDocument(filePath);
        if (document is null) return [];

        var formattedDocument = await Formatter.FormatAsync(document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var changes = await formattedDocument.GetTextChangesAsync(document, cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        var edits = new List<TextEdit>();
        foreach (var change in changes)
        {
            edits.Add(new TextEdit
            {
                Range = SpanToRange(change.Span, text),
                NewText = change.NewText ?? "",
            });
        }
        return edits;
    }

    // ─── Document Sync ──────────────────────────────────────────────────────

    public void UpdateDocumentContent(string filePath, string content)
    {
        if (_solution is null || _workspace is null) return;

        if (!_documentMap.TryGetValue(NormalizePath(filePath), out var docId)) return;

        var sourceText = SourceText.From(content);
        _solution = _solution.WithDocumentText(docId, sourceText);
    }

    public void UnloadWorkspace()
    {
        if (_workspace is not null)
        {
            _workspace.CloseSolution();
            _workspace.Dispose();
            _workspace = null;
        }
        _solution = null;
        _documentMap.Clear();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        UnloadWorkspace();
        return ValueTask.CompletedTask;
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    private static void EnsureMSBuildRegistered()
    {
        if (_locatorRegistered) return;
        lock (LocatorLock)
        {
            if (_locatorRegistered) return;
            MSBuildLocator.RegisterDefaults();
            _locatorRegistered = true;
            Logger.Information("[Roslyn] MSBuild registered: {Version}",
                MSBuildLocator.QueryVisualStudioInstances().FirstOrDefault()?.Version);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MSBuildWorkspace CreateWorkspace()
    {
        return MSBuildWorkspace.Create();
    }

    private void RebuildDocumentMap()
    {
        _documentMap.Clear();
        if (_solution is null) return;

        foreach (var project in _solution.Projects)
        foreach (var document in project.Documents)
        {
            if (document.FilePath is not null)
                _documentMap.TryAdd(NormalizePath(document.FilePath), document.Id);
        }
    }

    private Document? FindDocument(string filePath)
    {
        if (_solution is null) return null;
        return _documentMap.TryGetValue(NormalizePath(filePath), out var docId)
            ? _solution.GetDocument(docId)
            : null;
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('/', '\\');

    private static int GetOffset(SourceText text, int line, int column)
    {
        if (line < 0 || line >= text.Lines.Count) return -1;
        var textLine = text.Lines[line];
        return textLine.Start + Math.Min(column, textLine.Span.Length);
    }

    private static NvsRange SpanToRange(TextSpan span, SourceText text)
    {
        var start = text.Lines.GetLineFromPosition(span.Start);
        var end = text.Lines.GetLineFromPosition(span.End);
        return new NvsRange
        {
            Start = new Position { Line = start.LineNumber, Column = span.Start - start.Start },
            End = new Position { Line = end.LineNumber, Column = span.End - end.Start },
        };
    }

    private static string? ExtractXmlSummary(string? xmlDoc)
    {
        if (string.IsNullOrWhiteSpace(xmlDoc)) return null;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlDoc);
            var summary = doc.Descendants("summary").FirstOrDefault()?.Value.Trim();
            return string.IsNullOrEmpty(summary) ? null : summary;
        }
        catch
        {
            return null;
        }
    }

    private static NvsDiagnosticSeverity MapSeverity(Microsoft.CodeAnalysis.DiagnosticSeverity severity) => severity switch
    {
        Microsoft.CodeAnalysis.DiagnosticSeverity.Error => NvsDiagnosticSeverity.Error,
        Microsoft.CodeAnalysis.DiagnosticSeverity.Warning => NvsDiagnosticSeverity.Warning,
        Microsoft.CodeAnalysis.DiagnosticSeverity.Info => NvsDiagnosticSeverity.Information,
        _ => NvsDiagnosticSeverity.Hint,
    };

    private static CompletionItem MapCompletionItem(Microsoft.CodeAnalysis.Completion.CompletionItem item)
    {
        return new CompletionItem
        {
            Label = item.DisplayText,
            InsertText = item.DisplayText,
            Detail = item.InlineDescription,
            Kind = MapKind(item.Tags),
            IsSnippet = item.Tags.Contains("Snippet"),
        };
    }

    private static CompletionItemKind MapKind(ImmutableArray<string> tags)
    {
        if (tags.Contains("Method") || tags.Contains("ExtensionMethod")) return CompletionItemKind.Method;
        if (tags.Contains("Class")) return CompletionItemKind.Class;
        if (tags.Contains("Interface")) return CompletionItemKind.Interface;
        if (tags.Contains("Property")) return CompletionItemKind.Property;
        if (tags.Contains("Field")) return CompletionItemKind.Field;
        if (tags.Contains("Local") || tags.Contains("Parameter")) return CompletionItemKind.Variable;
        if (tags.Contains("Keyword")) return CompletionItemKind.Keyword;
        if (tags.Contains("Namespace") || tags.Contains("Module")) return CompletionItemKind.Module;
        if (tags.Contains("Constructor")) return CompletionItemKind.Constructor;
        if (tags.Contains("Snippet")) return CompletionItemKind.Snippet;
        if (tags.Contains("Delegate") || tags.Contains("Event")) return CompletionItemKind.Function;
        if (tags.Contains("Structure") || tags.Contains("Enum") || tags.Contains("EnumMember")) return CompletionItemKind.Class;
        return CompletionItemKind.Text;
    }
}
