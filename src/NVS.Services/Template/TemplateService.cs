using System.Diagnostics;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Template;

public sealed class TemplateService : ITemplateService
{
    private static readonly IReadOnlyList<ProjectTemplate> ProjectTemplates =
    [
        new()
        {
            ShortName = "console",
            DisplayName = "Console App",
            Description = "A project for creating a command-line application",
            DefaultLanguage = "C#",
            Tags = ["Console", "Common"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "classlib",
            DisplayName = "Class Library",
            Description = "A project for creating a class library",
            DefaultLanguage = "C#",
            Tags = ["Library", "Common"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "webapi",
            DisplayName = "ASP.NET Core Web API",
            Description = "A project for creating a RESTful HTTP service",
            DefaultLanguage = "C#",
            Tags = ["Web", "API"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "xunit",
            DisplayName = "xUnit Test Project",
            Description = "A project for creating xUnit tests",
            DefaultLanguage = "C#",
            Tags = ["Test", "xUnit"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "worker",
            DisplayName = "Worker Service",
            Description = "A project for creating a long-running background service",
            DefaultLanguage = "C#",
            Tags = ["Worker", "Background"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "blazorwasm",
            DisplayName = "Blazor WebAssembly App",
            Description = "A project for creating a Blazor WebAssembly client-side app",
            DefaultLanguage = "C#",
            Tags = ["Web", "Blazor"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "grpc",
            DisplayName = "gRPC Service",
            Description = "A project for creating a gRPC service",
            DefaultLanguage = "C#",
            Tags = ["Web", "gRPC"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
            ProjectSystem = ProjectSystem.DotNet,
        },
        new()
        {
            ShortName = "java-console",
            DisplayName = "Java Console App",
            Description = "A Maven project for creating a Java command-line application",
            DefaultLanguage = "Java",
            Tags = ["Console", "Maven"],
            ProjectSystem = ProjectSystem.Maven,
        },
        new()
        {
            ShortName = "java-library",
            DisplayName = "Java Library",
            Description = "A Maven project for creating a reusable Java library",
            DefaultLanguage = "Java",
            Tags = ["Library", "Maven"],
            ProjectSystem = ProjectSystem.Maven,
        },
        new()
        {
            ShortName = "php-console",
            DisplayName = "PHP Console App",
            Description = "A Composer project for creating a PHP command-line application",
            DefaultLanguage = "PHP",
            Tags = ["Console", "Composer"],
            ProjectSystem = ProjectSystem.Composer,
        },
        new()
        {
            ShortName = "php-library",
            DisplayName = "PHP Library",
            Description = "A Composer project for creating a reusable PHP library",
            DefaultLanguage = "PHP",
            Tags = ["Library", "Composer"],
            ProjectSystem = ProjectSystem.Composer,
        },
    ];

    private static readonly IReadOnlyList<FileTemplate> FileTemplates =
    [
        new()
        {
            Id = "class",
            DisplayName = "Class",
            DefaultFileName = "NewClass",
            Extension = ".cs",
            Icon = "🟢",
            ContentTemplate = """
                namespace {{Namespace}};

                public class {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "interface",
            DisplayName = "Interface",
            DefaultFileName = "INewInterface",
            Extension = ".cs",
            Icon = "🔵",
            ContentTemplate = """
                namespace {{Namespace}};

                public interface {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "record",
            DisplayName = "Record",
            DefaultFileName = "NewRecord",
            Extension = ".cs",
            Icon = "🟣",
            ContentTemplate = """
                namespace {{Namespace}};

                public sealed record {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "enum",
            DisplayName = "Enum",
            DefaultFileName = "NewEnum",
            Extension = ".cs",
            Icon = "🟠",
            ContentTemplate = """
                namespace {{Namespace}};

                public enum {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "struct",
            DisplayName = "Struct",
            DefaultFileName = "NewStruct",
            Extension = ".cs",
            Icon = "🔶",
            ContentTemplate = """
                namespace {{Namespace}};

                public readonly struct {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "exception",
            DisplayName = "Exception",
            DefaultFileName = "NewException",
            Extension = ".cs",
            Icon = "🔴",
            ContentTemplate = """
                namespace {{Namespace}};

                public class {{Name}} : Exception
                {
                    public {{Name}}() { }

                    public {{Name}}(string message) : base(message) { }

                    public {{Name}}(string message, Exception innerException) : base(message, innerException) { }
                }
                """,
        },
        new()
        {
            Id = "static-class",
            DisplayName = "Static Class",
            DefaultFileName = "NewStaticClass",
            Extension = ".cs",
            Icon = "⚡",
            ContentTemplate = """
                namespace {{Namespace}};

                public static class {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "abstract-class",
            DisplayName = "Abstract Class",
            DefaultFileName = "NewAbstractClass",
            Extension = ".cs",
            Icon = "🟤",
            ContentTemplate = """
                namespace {{Namespace}};

                public abstract class {{Name}}
                {
                }
                """,
        },
        // ── Java file templates ──
        new()
        {
            Id = "java-class",
            DisplayName = "Java Class",
            DefaultFileName = "NewClass",
            Extension = ".java",
            Icon = "☕",
            ContentTemplate = """
                package {{Namespace}};

                public class {{Name}} {
                }
                """,
        },
        new()
        {
            Id = "java-interface",
            DisplayName = "Java Interface",
            DefaultFileName = "NewInterface",
            Extension = ".java",
            Icon = "☕",
            ContentTemplate = """
                package {{Namespace}};

                public interface {{Name}} {
                }
                """,
        },
        new()
        {
            Id = "java-enum",
            DisplayName = "Java Enum",
            DefaultFileName = "NewEnum",
            Extension = ".java",
            Icon = "☕",
            ContentTemplate = """
                package {{Namespace}};

                public enum {{Name}} {
                }
                """,
        },
        new()
        {
            Id = "java-record",
            DisplayName = "Java Record",
            DefaultFileName = "NewRecord",
            Extension = ".java",
            Icon = "☕",
            ContentTemplate = """
                package {{Namespace}};

                public record {{Name}}() {
                }
                """,
        },
        // ── PHP file templates ──
        new()
        {
            Id = "php-class",
            DisplayName = "PHP Class",
            DefaultFileName = "NewClass",
            Extension = ".php",
            Icon = "🐘",
            ContentTemplate = """
                <?php

                namespace {{Namespace}};

                class {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "php-interface",
            DisplayName = "PHP Interface",
            DefaultFileName = "NewInterface",
            Extension = ".php",
            Icon = "🐘",
            ContentTemplate = """
                <?php

                namespace {{Namespace}};

                interface {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "php-enum",
            DisplayName = "PHP Enum",
            DefaultFileName = "NewEnum",
            Extension = ".php",
            Icon = "🐘",
            ContentTemplate = """
                <?php

                namespace {{Namespace}};

                enum {{Name}}
                {
                }
                """,
        },
        new()
        {
            Id = "php-trait",
            DisplayName = "PHP Trait",
            DefaultFileName = "NewTrait",
            Extension = ".php",
            Icon = "🐘",
            ContentTemplate = """
                <?php

                namespace {{Namespace}};

                trait {{Name}}
                {
                }
                """,
        },
    ];

    public IReadOnlyList<ProjectTemplate> GetProjectTemplates() => ProjectTemplates;

    public IReadOnlyList<FileTemplate> GetFileTemplates() => FileTemplates;

    public async Task<string> CreateProjectAsync(
        string templateShortName,
        string projectName,
        string outputDirectory,
        string? framework = null,
        bool createSolution = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateShortName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var template = ProjectTemplates.FirstOrDefault(t => t.ShortName == templateShortName)
            ?? throw new ArgumentException($"Unknown project template '{templateShortName}'.", nameof(templateShortName));

        return template.ProjectSystem switch
        {
            ProjectSystem.Maven => await CreateMavenProjectAsync(template, projectName, outputDirectory, cancellationToken).ConfigureAwait(false),
            ProjectSystem.Composer => await CreateComposerProjectAsync(template, projectName, outputDirectory, cancellationToken).ConfigureAwait(false),
            _ => await CreateDotNetProjectAsync(template, projectName, outputDirectory, framework, createSolution, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<string> CreateDotNetProjectAsync(
        ProjectTemplate template,
        string projectName,
        string outputDirectory,
        string? framework,
        bool createSolution,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDir);

        var args = $"new {template.ShortName} --name {projectName} --output \"{projectDir}\"";
        if (!string.IsNullOrWhiteSpace(framework))
            args += $" --framework {framework}";

        await RunProcessAsync("dotnet", args, outputDirectory, "dotnet new", cancellationToken).ConfigureAwait(false);

        if (createSolution)
        {
            await RunProcessAsync("dotnet",
                $"new sln --name \"{projectName}\" --output \"{projectDir}\"",
                projectDir, "dotnet new sln", cancellationToken).ConfigureAwait(false);

            var csprojFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                var slnPath = Directory.GetFiles(projectDir, "*.slnx", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(projectDir, "*.sln", SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();

                if (slnPath is not null)
                {
                    await RunProcessAsync("dotnet",
                        $"sln \"{slnPath}\" add \"{csprojFiles[0]}\"",
                        projectDir, "dotnet sln add", cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return projectDir;
    }

    internal static async Task<string> CreateMavenProjectAsync(
        ProjectTemplate template,
        string projectName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDir);

        var groupId = "com.example";
        var artifactId = projectName.ToLowerInvariant().Replace(' ', '-');
        var packagePath = Path.Combine(projectDir, "src", "main", "java", "com", "example");
        var testPath = Path.Combine(projectDir, "src", "test", "java", "com", "example");
        Directory.CreateDirectory(packagePath);
        Directory.CreateDirectory(testPath);

        var isConsole = template.ShortName == "java-console";
        var packaging = isConsole ? "jar" : "jar";
        var mainClass = isConsole ? $"\n    <exec.mainClass>{groupId}.App</exec.mainClass>" : "";

        var pomXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <project xmlns="http://maven.apache.org/POM/4.0.0"
                     xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                     xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd">
                <modelVersion>4.0.0</modelVersion>

                <groupId>{groupId}</groupId>
                <artifactId>{artifactId}</artifactId>
                <version>1.0-SNAPSHOT</version>
                <packaging>{packaging}</packaging>

                <properties>
                    <maven.compiler.source>21</maven.compiler.source>
                    <maven.compiler.target>21</maven.compiler.target>
                    <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>{mainClass}
                </properties>
            </project>
            """;

        await File.WriteAllTextAsync(Path.Combine(projectDir, "pom.xml"), pomXml, cancellationToken).ConfigureAwait(false);

        if (isConsole)
        {
            var appJava = """
                package com.example;

                public class App {
                    public static void main(String[] args) {
                        System.out.println("Hello, World!");
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(packagePath, "App.java"), appJava, cancellationToken).ConfigureAwait(false);
        }

        var testClassName = isConsole ? "AppTest" : "LibraryTest";
        var testJava = $$"""
            package com.example;

            public class {{testClassName}} {
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(testPath, testClassName + ".java"),
            testJava, cancellationToken).ConfigureAwait(false);

        return projectDir;
    }

    internal static async Task<string> CreateComposerProjectAsync(
        ProjectTemplate template,
        string projectName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var projectDir = Path.Combine(outputDirectory, projectName);
        var srcDir = Path.Combine(projectDir, "src");
        var testsDir = Path.Combine(projectDir, "tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testsDir);

        var vendorName = "myvendor";
        var packageName = projectName.ToLowerInvariant().Replace(' ', '-');
        var isConsole = template.ShortName == "php-console";
        var namespaceName = ToPascalCase(projectName);

        var composerJson = $$"""
            {
                "name": "{{vendorName}}/{{packageName}}",
                "description": "{{template.Description}}",
                "type": "{{(isConsole ? "project" : "library")}}",
                "require": {
                    "php": ">=8.2"
                },
                "autoload": {
                    "psr-4": {
                        "{{namespaceName}}\\": "src/"
                    }
                },
                "autoload-dev": {
                    "psr-4": {
                        "{{namespaceName}}\\Tests\\": "tests/"
                    }
                }
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(projectDir, "composer.json"), composerJson, cancellationToken).ConfigureAwait(false);

        if (isConsole)
        {
            var binDir = Path.Combine(projectDir, "bin");
            Directory.CreateDirectory(binDir);

            var entryPoint = $$"""
                #!/usr/bin/env php
                <?php

                declare(strict_types=1);

                require __DIR__ . '/../vendor/autoload.php';

                use {{namespaceName}}\App;

                $app = new App();
                $app->run();
                """;
            await File.WriteAllTextAsync(Path.Combine(binDir, "app.php"), entryPoint, cancellationToken).ConfigureAwait(false);

            var appPhp = $$"""
                <?php

                declare(strict_types=1);

                namespace {{namespaceName}};

                class App
                {
                    public function run(): void
                    {
                        echo "Hello, World!" . PHP_EOL;
                    }
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(srcDir, "App.php"), appPhp, cancellationToken).ConfigureAwait(false);
        }

        return projectDir;
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static async Task RunProcessAsync(
        string fileName, string arguments, string workingDirectory, string description, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        _ = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{description} failed (exit code {process.ExitCode}): {stderr.Trim()}");
    }

    public async Task<string> CreateFileFromTemplateAsync(
        string templateId,
        string fileName,
        string directory,
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var template = FileTemplates.FirstOrDefault(t => t.Id == templateId)
            ?? throw new ArgumentException($"Unknown file template '{templateId}'.", nameof(templateId));

        var typeName = Path.GetFileNameWithoutExtension(fileName);
        var content = template.ContentTemplate
            .Replace("{{Namespace}}", namespaceName)
            .Replace("{{Name}}", typeName);

        var filePath = Path.Combine(directory, typeName + template.Extension);

        if (File.Exists(filePath))
            throw new InvalidOperationException($"File already exists: {filePath}");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);

        return filePath;
    }

    /// <summary>
    /// Infers a namespace from a file path relative to a project root.
    /// E.g. "C:\MyApp\Models" with root "C:\MyApp" → "MyApp.Models"
    /// </summary>
    public static string InferNamespace(string directory, string? projectRoot, string? rootNamespace = null)
    {
        var baseName = rootNamespace
            ?? (projectRoot is not null ? Path.GetFileName(projectRoot) : Path.GetFileName(directory))
            ?? "MyNamespace";

        if (projectRoot is null || string.Equals(directory, projectRoot, StringComparison.OrdinalIgnoreCase))
            return baseName;

        var relative = Path.GetRelativePath(projectRoot, directory);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
            return baseName;

        var parts = relative
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length > 0
            ? $"{baseName}.{string.Join('.', parts)}"
            : baseName;
    }
}
