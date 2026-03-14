using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NVS.Core.Interfaces;

namespace NVS.Services.Metrics;

/// <summary>
/// Calculates code metrics for C# files using Roslyn syntax tree analysis.
/// Metrics include cyclomatic complexity, LOC, maintainability index,
/// class coupling, and depth of inheritance.
/// </summary>
public sealed class CodeMetricsService : ICodeMetricsService
{
    public async Task<FileMetrics> CalculateFileMetricsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var source = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        return CalculateFileMetrics(filePath, source);
    }

    public async Task<ProjectMetrics> CalculateProjectMetricsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var projectDir = Path.GetDirectoryName(projectPath) ?? projectPath;
        var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        var fileMetricsList = new List<FileMetrics>();
        foreach (var file in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metrics = await CalculateFileMetricsAsync(file, cancellationToken).ConfigureAwait(false);
            fileMetricsList.Add(metrics);
        }

        var totalLines = new LineMetrics
        {
            Total = fileMetricsList.Sum(f => f.Lines.Total),
            Blank = fileMetricsList.Sum(f => f.Lines.Blank),
            Comment = fileMetricsList.Sum(f => f.Lines.Comment),
            Executable = fileMetricsList.Sum(f => f.Lines.Executable),
        };

        var allMethods = fileMetricsList
            .SelectMany(f => f.Types)
            .SelectMany(t => t.Methods)
            .ToList();

        return new ProjectMetrics
        {
            ProjectName = Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath = projectPath,
            Files = fileMetricsList,
            TotalLines = totalLines,
            TotalTypes = fileMetricsList.Sum(f => f.Types.Count),
            TotalMethods = allMethods.Count,
            AverageCyclomaticComplexity = allMethods.Count > 0
                ? allMethods.Average(m => m.CyclomaticComplexity) : 0,
            AverageMaintainabilityIndex = allMethods.Count > 0
                ? allMethods.Average(m => m.MaintainabilityIndex) : 0,
        };
    }

    internal static FileMetrics CalculateFileMetrics(string filePath, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: default);
        var root = tree.GetCompilationUnitRoot();

        var lines = CalculateLineMetrics(source, root);
        var types = ExtractTypeMetrics(root);

        return new FileMetrics
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Lines = lines,
            Types = types,
        };
    }

    // ─── Line Metrics ───────────────────────────────────────────────────────

    internal static LineMetrics CalculateLineMetrics(string source, SyntaxNode root)
    {
        var allLines = source.Split('\n');
        var total = allLines.Length;
        var blank = 0;
        var comment = 0;

        foreach (var line in allLines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                blank++;
            else if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                comment++;
        }

        // Count comment lines from trivia more accurately
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                var triviaLines = trivia.ToString().Split('\n').Length;
                // Subtract 1 because the first line was likely counted already
                comment += Math.Max(0, triviaLines - 1);
            }
        }

        var executable = total - blank - comment;

        return new LineMetrics
        {
            Total = total,
            Blank = blank,
            Comment = comment,
            Executable = Math.Max(0, executable),
        };
    }

    // ─── Type Metrics ───────────────────────────────────────────────────────

    internal static IReadOnlyList<TypeMetrics> ExtractTypeMetrics(CompilationUnitSyntax root)
    {
        var types = new List<TypeMetrics>();

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var kind = typeDecl switch
            {
                ClassDeclarationSyntax => TypeKindMetric.Class,
                StructDeclarationSyntax => TypeKindMetric.Struct,
                InterfaceDeclarationSyntax => TypeKindMetric.Interface,
                RecordDeclarationSyntax => TypeKindMetric.Record,
                _ => TypeKindMetric.Class,
            };

            var namespaceName = GetNamespace(typeDecl);
            var fullName = string.IsNullOrEmpty(namespaceName)
                ? typeDecl.Identifier.Text
                : $"{namespaceName}.{typeDecl.Identifier.Text}";

            var methods = ExtractMethodMetrics(typeDecl);
            var coupling = CalculateClassCoupling(typeDecl);
            var doi = CalculateDepthOfInheritance(typeDecl);
            var line = typeDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            types.Add(new TypeMetrics
            {
                Name = typeDecl.Identifier.Text,
                FullName = fullName,
                Kind = kind,
                Line = line,
                DepthOfInheritance = doi,
                ClassCoupling = coupling,
                Methods = methods,
            });
        }

        // Also extract enum declarations
        foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var namespaceName = GetNamespace(enumDecl);
            var fullName = string.IsNullOrEmpty(namespaceName)
                ? enumDecl.Identifier.Text
                : $"{namespaceName}.{enumDecl.Identifier.Text}";
            var line = enumDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            types.Add(new TypeMetrics
            {
                Name = enumDecl.Identifier.Text,
                FullName = fullName,
                Kind = TypeKindMetric.Enum,
                Line = line,
            });
        }

        return types;
    }

    // ─── Method Metrics ─────────────────────────────────────────────────────

    internal static IReadOnlyList<MethodMetrics> ExtractMethodMetrics(TypeDeclarationSyntax typeDecl)
    {
        var methods = new List<MethodMetrics>();

        foreach (var member in typeDecl.Members)
        {
            string? name = null;
            BlockSyntax? body = null;
            ArrowExpressionClauseSyntax? expressionBody = null;
            int line = 0;

            switch (member)
            {
                case MethodDeclarationSyntax method:
                    name = method.Identifier.Text;
                    body = method.Body;
                    expressionBody = method.ExpressionBody;
                    line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    break;
                case ConstructorDeclarationSyntax ctor:
                    name = ctor.Identifier.Text + " (ctor)";
                    body = ctor.Body;
                    expressionBody = ctor.ExpressionBody;
                    line = ctor.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    break;
                case PropertyDeclarationSyntax prop when prop.AccessorList?.Accessors.Any(a => a.Body is not null) == true:
                    name = prop.Identifier.Text + " (property)";
                    body = prop.AccessorList!.Accessors.First(a => a.Body is not null).Body;
                    line = prop.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    break;
            }

            if (name is null)
                continue;

            var complexity = CalculateCyclomaticComplexity(body, expressionBody);
            var methodSource = member.ToString();
            var methodLines = CalculateMethodLineMetrics(methodSource);
            var maintainability = CalculateMaintainabilityIndex(complexity, methodLines);

            methods.Add(new MethodMetrics
            {
                Name = name,
                Line = line,
                CyclomaticComplexity = complexity,
                Lines = methodLines,
                MaintainabilityIndex = maintainability,
            });
        }

        return methods;
    }

    // ─── Cyclomatic Complexity ──────────────────────────────────────────────

    /// <summary>
    /// Calculates cyclomatic complexity by counting decision points.
    /// Each method starts at 1, and each branch/decision adds 1.
    /// </summary>
    internal static int CalculateCyclomaticComplexity(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody)
    {
        if (body is null && expressionBody is null)
            return 1;

        var complexity = 1;
        SyntaxNode node = body as SyntaxNode ?? expressionBody!;

        foreach (var descendant in node.DescendantNodes())
        {
            switch (descendant)
            {
                case IfStatementSyntax:
                case ConditionalExpressionSyntax:       // ? :
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CatchClauseSyntax:
                case ConditionalAccessExpressionSyntax:  // ?.
                    complexity++;
                    break;
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression) ||
                    binary.IsKind(SyntaxKind.LogicalOrExpression) ||
                    binary.IsKind(SyntaxKind.CoalesceExpression):
                    complexity++;
                    break;
                case SwitchExpressionArmSyntax:
                    complexity++;
                    break;
            }
        }

        return complexity;
    }

    // ─── Maintainability Index ──────────────────────────────────────────────

    /// <summary>
    /// Simplified Maintainability Index (0–100 scale).
    /// Based on: MI = MAX(0, (171 - 5.2 * ln(HV) - 0.23 * CC - 16.2 * ln(LOC)) * 100 / 171)
    /// Using simplified Halstead Volume approximation based on LOC.
    /// </summary>
    internal static double CalculateMaintainabilityIndex(int cyclomaticComplexity, LineMetrics lines)
    {
        var loc = Math.Max(1, lines.Executable);
        var halsteadVolume = loc * Math.Log2(Math.Max(2, loc)); // Simplified approximation

        var mi = 171.0
            - 5.2 * Math.Log(Math.Max(1, halsteadVolume))
            - 0.23 * cyclomaticComplexity
            - 16.2 * Math.Log(Math.Max(1, loc));

        return Math.Max(0, Math.Min(100, mi * 100.0 / 171.0));
    }

    // ─── Class Coupling ─────────────────────────────────────────────────────

    /// <summary>
    /// Counts unique type references in the class (excluding self and primitives).
    /// </summary>
    internal static int CalculateClassCoupling(TypeDeclarationSyntax typeDecl)
    {
        var referencedTypes = new HashSet<string>();
        var selfName = typeDecl.Identifier.Text;

        foreach (var node in typeDecl.DescendantNodes())
        {
            switch (node)
            {
                case IdentifierNameSyntax id when char.IsUpper(id.Identifier.Text[0]):
                    var name = id.Identifier.Text;
                    if (name != selfName && !IsPrimitiveType(name))
                        referencedTypes.Add(name);
                    break;
                case GenericNameSyntax generic:
                    referencedTypes.Add(generic.Identifier.Text);
                    break;
                case QualifiedNameSyntax qualified:
                    referencedTypes.Add(qualified.Right.Identifier.Text);
                    break;
            }
        }

        return referencedTypes.Count;
    }

    // ─── Depth of Inheritance ───────────────────────────────────────────────

    /// <summary>
    /// Counts base types in the syntax (without semantic analysis).
    /// Returns 1 for a base class, 0 for none (syntactic only — no Object counting).
    /// </summary>
    internal static int CalculateDepthOfInheritance(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return 0;

        // Count non-interface bases (heuristic: interfaces start with I and uppercase)
        return typeDecl.BaseList.Types.Count(t =>
        {
            var typeName = t.Type.ToString();
            return !typeName.StartsWith('I') || typeName.Length < 2 || !char.IsUpper(typeName[1]);
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static LineMetrics CalculateMethodLineMetrics(string source)
    {
        var lines = source.Split('\n');
        var total = lines.Length;
        var blank = 0;
        var comment = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                blank++;
            else if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                comment++;
        }

        return new LineMetrics
        {
            Total = total,
            Blank = blank,
            Comment = comment,
            Executable = Math.Max(0, total - blank - comment),
        };
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
            current = current.Parent;
        }
        return "";
    }

    private static bool IsPrimitiveType(string name) => name switch
    {
        "String" or "Int32" or "Int64" or "Boolean" or "Double" or "Float" or "Byte"
            or "Char" or "Decimal" or "Object" or "Void" or "Task" or "CancellationToken"
            or "Guid" or "DateTime" or "TimeSpan" or "Type" => true,
        _ => false,
    };
}
