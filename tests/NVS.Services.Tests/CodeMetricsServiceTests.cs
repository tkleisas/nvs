using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NVS.Core.Interfaces;
using NVS.Services.Metrics;

namespace NVS.Services.Tests;

public sealed class CodeMetricsServiceTests
{
    // ─── Cyclomatic Complexity ──────────────────────────────────────────────

    [Fact]
    public void CyclomaticComplexity_SimpleMethod_ShouldBeOne()
    {
        var body = ParseMethodBody("""
            {
                Console.WriteLine("Hello");
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(1);
    }

    [Fact]
    public void CyclomaticComplexity_WithIfStatement_ShouldBeTwo()
    {
        var body = ParseMethodBody("""
            {
                if (x > 0)
                    Console.WriteLine("positive");
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(2);
    }

    [Fact]
    public void CyclomaticComplexity_WithIfElseIfElse_ShouldBeThree()
    {
        var body = ParseMethodBody("""
            {
                if (x > 0)
                    Console.WriteLine("positive");
                else if (x < 0)
                    Console.WriteLine("negative");
                else
                    Console.WriteLine("zero");
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(3);
    }

    [Fact]
    public void CyclomaticComplexity_WithForLoop_ShouldBeTwo()
    {
        var body = ParseMethodBody("""
            {
                for (int i = 0; i < 10; i++)
                    Console.WriteLine(i);
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(2);
    }

    [Fact]
    public void CyclomaticComplexity_WithForeachLoop_ShouldBeTwo()
    {
        var body = ParseMethodBody("""
            {
                foreach (var item in items)
                    Console.WriteLine(item);
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(2);
    }

    [Fact]
    public void CyclomaticComplexity_WithWhileLoop_ShouldBeTwo()
    {
        var body = ParseMethodBody("""
            {
                while (x > 0)
                    x--;
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(2);
    }

    [Fact]
    public void CyclomaticComplexity_WithLogicalOperators_ShouldCount()
    {
        var body = ParseMethodBody("""
            {
                if (a && b || c)
                    Console.WriteLine("complex");
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        // 1 (base) + 1 (if) + 1 (&&) + 1 (||) = 4
        result.Should().Be(4);
    }

    [Fact]
    public void CyclomaticComplexity_WithTryCatch_ShouldCount()
    {
        var body = ParseMethodBody("""
            {
                try
                {
                    DoSomething();
                }
                catch (Exception)
                {
                    HandleError();
                }
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        result.Should().Be(2);
    }

    [Fact]
    public void CyclomaticComplexity_WithSwitchExpression_ShouldCountArms()
    {
        var body = ParseMethodBody("""
            {
                var result = x switch
                {
                    1 => "one",
                    2 => "two",
                    _ => "other",
                };
            }
            """);

        var result = CodeMetricsService.CalculateCyclomaticComplexity(body, null);

        // 1 (base) + 3 arms = 4
        result.Should().Be(4);
    }

    [Fact]
    public void CyclomaticComplexity_WithNullBody_ShouldBeOne()
    {
        var result = CodeMetricsService.CalculateCyclomaticComplexity(null, null);

        result.Should().Be(1);
    }

    // ─── Line Metrics ───────────────────────────────────────────────────────

    [Fact]
    public void CalculateLineMetrics_ShouldCountCorrectly()
    {
        var source = """
            // Header comment
            using System;

            namespace Test
            {
                /// <summary>Doc comment</summary>
                public class MyClass
                {
                    public void Method()
                    {
                        Console.WriteLine("Hello");
                    }
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        var result = CodeMetricsService.CalculateLineMetrics(source, root);

        result.Total.Should().BeGreaterThan(10);
        result.Blank.Should().BeGreaterThan(0);
        result.Comment.Should().BeGreaterThan(0);
        result.Executable.Should().BeGreaterThan(0);
    }

    // ─── Type Extraction ────────────────────────────────────────────────────

    [Fact]
    public void ExtractTypeMetrics_ShouldFindClassesAndMethods()
    {
        var source = """
            namespace Test
            {
                public class Calculator
                {
                    public int Add(int a, int b)
                    {
                        return a + b;
                    }

                    public int Multiply(int a, int b)
                    {
                        return a * b;
                    }
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        var types = CodeMetricsService.ExtractTypeMetrics(root);

        types.Should().HaveCount(1);
        types[0].Name.Should().Be("Calculator");
        types[0].FullName.Should().Be("Test.Calculator");
        types[0].Kind.Should().Be(TypeKindMetric.Class);
        types[0].Methods.Should().HaveCount(2);
        types[0].Methods[0].Name.Should().Be("Add");
        types[0].Methods[1].Name.Should().Be("Multiply");
    }

    [Fact]
    public void ExtractTypeMetrics_ShouldFindEnums()
    {
        var source = """
            namespace Test
            {
                public enum Color { Red, Green, Blue }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        var types = CodeMetricsService.ExtractTypeMetrics(root);

        types.Should().HaveCount(1);
        types[0].Name.Should().Be("Color");
        types[0].Kind.Should().Be(TypeKindMetric.Enum);
    }

    [Fact]
    public void ExtractTypeMetrics_ShouldFindRecords()
    {
        var source = """
            namespace Test
            {
                public record Person(string Name, int Age);
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        var types = CodeMetricsService.ExtractTypeMetrics(root);

        types.Should().HaveCount(1);
        types[0].Name.Should().Be("Person");
        types[0].Kind.Should().Be(TypeKindMetric.Record);
    }

    [Fact]
    public void ExtractTypeMetrics_ShouldFindInterfaces()
    {
        var source = """
            namespace Test
            {
                public interface IService
                {
                    void DoWork();
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetCompilationUnitRoot();
        var types = CodeMetricsService.ExtractTypeMetrics(root);

        types.Should().HaveCount(1);
        types[0].Name.Should().Be("IService");
        types[0].Kind.Should().Be(TypeKindMetric.Interface);
    }

    // ─── Class Coupling ─────────────────────────────────────────────────────

    [Fact]
    public void ClassCoupling_WithDependencies_ShouldCountUniqueTypes()
    {
        var source = """
            public class MyService
            {
                private readonly ILogger _logger;
                private readonly HttpClient _client;

                public MyService(ILogger logger, HttpClient client)
                {
                    _logger = logger;
                    _client = client;
                }

                public List<string> GetItems()
                {
                    return new List<string>();
                }
            }
            """;

        var typeDecl = ParseClass(source);
        var coupling = CodeMetricsService.CalculateClassCoupling(typeDecl);

        // ILogger, HttpClient, List (at minimum)
        coupling.Should().BeGreaterThanOrEqualTo(3);
    }

    // ─── Depth of Inheritance ───────────────────────────────────────────────

    [Fact]
    public void DepthOfInheritance_WithNoBase_ShouldBeZero()
    {
        var source = """
            public class SimpleClass
            {
            }
            """;

        var typeDecl = ParseClass(source);
        var doi = CodeMetricsService.CalculateDepthOfInheritance(typeDecl);

        doi.Should().Be(0);
    }

    [Fact]
    public void DepthOfInheritance_WithBaseClass_ShouldBeOne()
    {
        var source = """
            public class DerivedClass : BaseClass
            {
            }
            """;

        var typeDecl = ParseClass(source);
        var doi = CodeMetricsService.CalculateDepthOfInheritance(typeDecl);

        doi.Should().Be(1);
    }

    [Fact]
    public void DepthOfInheritance_WithInterfaceOnly_ShouldBeZero()
    {
        var source = """
            public class MyClass : IDisposable
            {
                public void Dispose() { }
            }
            """;

        var typeDecl = ParseClass(source);
        var doi = CodeMetricsService.CalculateDepthOfInheritance(typeDecl);

        doi.Should().Be(0);
    }

    // ─── Maintainability Index ──────────────────────────────────────────────

    [Fact]
    public void MaintainabilityIndex_SimpleMethod_ShouldBeHigh()
    {
        var lines = new LineMetrics { Total = 5, Blank = 1, Comment = 0, Executable = 4 };

        var mi = CodeMetricsService.CalculateMaintainabilityIndex(1, lines);

        mi.Should().BeGreaterThan(50);
    }

    [Fact]
    public void MaintainabilityIndex_ComplexMethod_ShouldBeLower()
    {
        var simpleLines = new LineMetrics { Total = 5, Blank = 1, Comment = 0, Executable = 4 };
        var complexLines = new LineMetrics { Total = 100, Blank = 10, Comment = 5, Executable = 85 };

        var simpleMi = CodeMetricsService.CalculateMaintainabilityIndex(1, simpleLines);
        var complexMi = CodeMetricsService.CalculateMaintainabilityIndex(20, complexLines);

        complexMi.Should().BeLessThan(simpleMi);
    }

    [Fact]
    public void MaintainabilityIndex_ShouldBeInRange()
    {
        var lines = new LineMetrics { Total = 50, Blank = 5, Comment = 3, Executable = 42 };

        var mi = CodeMetricsService.CalculateMaintainabilityIndex(10, lines);

        mi.Should().BeGreaterThanOrEqualTo(0);
        mi.Should().BeLessThanOrEqualTo(100);
    }

    // ─── Full File Metrics ──────────────────────────────────────────────────

    [Fact]
    public void CalculateFileMetrics_ShouldReturnCompleteMetrics()
    {
        var source = """
            using System;

            namespace MyApp
            {
                public class Calculator
                {
                    public int Add(int a, int b)
                    {
                        return a + b;
                    }

                    public int Divide(int a, int b)
                    {
                        if (b == 0)
                            throw new DivideByZeroException();
                        return a / b;
                    }
                }

                public enum Operation
                {
                    Add,
                    Subtract,
                    Multiply,
                    Divide,
                }
            }
            """;

        var result = CodeMetricsService.CalculateFileMetrics("test.cs", source);

        result.FileName.Should().Be("test.cs");
        result.Lines.Total.Should().BeGreaterThan(20);
        result.Types.Should().HaveCount(2); // Calculator + Operation

        var calculator = result.Types.First(t => t.Name == "Calculator");
        calculator.Kind.Should().Be(TypeKindMetric.Class);
        calculator.Methods.Should().HaveCount(2);

        var addMethod = calculator.Methods.First(m => m.Name == "Add");
        addMethod.CyclomaticComplexity.Should().Be(1);

        var divideMethod = calculator.Methods.First(m => m.Name == "Divide");
        divideMethod.CyclomaticComplexity.Should().Be(2); // 1 + if
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static BlockSyntax ParseMethodBody(string body)
    {
        var source = $"class C {{ void M() {body} }}";
        var tree = CSharpSyntaxTree.ParseText(source);
        return tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First()
            .Body!;
    }

    private static TypeDeclarationSyntax ParseClass(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        return tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First();
    }
}
