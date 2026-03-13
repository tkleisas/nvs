using System.Diagnostics;
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
        },
        new()
        {
            ShortName = "classlib",
            DisplayName = "Class Library",
            Description = "A project for creating a class library",
            DefaultLanguage = "C#",
            Tags = ["Library", "Common"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
        },
        new()
        {
            ShortName = "webapi",
            DisplayName = "ASP.NET Core Web API",
            Description = "A project for creating a RESTful HTTP service",
            DefaultLanguage = "C#",
            Tags = ["Web", "API"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
        },
        new()
        {
            ShortName = "xunit",
            DisplayName = "xUnit Test Project",
            Description = "A project for creating xUnit tests",
            DefaultLanguage = "C#",
            Tags = ["Test", "xUnit"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
        },
        new()
        {
            ShortName = "worker",
            DisplayName = "Worker Service",
            Description = "A project for creating a long-running background service",
            DefaultLanguage = "C#",
            Tags = ["Worker", "Background"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
        },
        new()
        {
            ShortName = "blazorwasm",
            DisplayName = "Blazor WebAssembly App",
            Description = "A project for creating a Blazor WebAssembly client-side app",
            DefaultLanguage = "C#",
            Tags = ["Web", "Blazor"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
        },
        new()
        {
            ShortName = "grpc",
            DisplayName = "gRPC Service",
            Description = "A project for creating a gRPC service",
            DefaultLanguage = "C#",
            Tags = ["Web", "gRPC"],
            Frameworks = ["net10.0", "net9.0", "net8.0"],
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

        var projectDir = Path.Combine(outputDirectory, projectName);
        Directory.CreateDirectory(projectDir);

        // Create the project
        var args = $"new {templateShortName} --name {projectName} --output \"{projectDir}\"";
        if (!string.IsNullOrWhiteSpace(framework))
            args += $" --framework {framework}";

        await RunDotnetAsync(args, outputDirectory, "dotnet new", cancellationToken).ConfigureAwait(false);

        if (createSolution)
        {
            // Create a solution file in the project directory
            await RunDotnetAsync(
                $"new sln --name \"{projectName}\" --output \"{projectDir}\"",
                projectDir, "dotnet new sln", cancellationToken).ConfigureAwait(false);

            // Add the project to the solution
            var csprojFiles = Directory.GetFiles(projectDir, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                // dotnet new sln creates .slnx on modern SDKs, fall back to .sln
                var slnPath = Directory.GetFiles(projectDir, "*.slnx", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(projectDir, "*.sln", SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();

                if (slnPath is not null)
                {
                    await RunDotnetAsync(
                        $"sln \"{slnPath}\" add \"{csprojFiles[0]}\"",
                        projectDir, "dotnet sln add", cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return projectDir;
    }

    private static async Task RunDotnetAsync(
        string arguments, string workingDirectory, string description, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
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
