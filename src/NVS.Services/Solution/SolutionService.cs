using System.Text.RegularExpressions;
using System.Xml.Linq;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Solution;

public sealed partial class SolutionService : ISolutionService
{
    private SolutionModel? _currentSolution;
    private readonly List<ProjectModel> _loadedProjects = [];

    public SolutionModel? CurrentSolution => _currentSolution;
    public bool IsSolutionLoaded => _currentSolution is not null;

    public event EventHandler<SolutionModel>? SolutionLoaded;
    public event EventHandler? SolutionClosed;

    public async Task<SolutionModel> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        if (!File.Exists(solutionPath))
            throw new FileNotFoundException("Solution file not found.", solutionPath);

        var extension = Path.GetExtension(solutionPath);
        var solution = extension.ToLowerInvariant() switch
        {
            ".slnx" => await ParseSlnxAsync(solutionPath, cancellationToken).ConfigureAwait(false),
            ".sln" => await ParseSlnAsync(solutionPath, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Solution format '{extension}' is not supported.")
        };

        _loadedProjects.Clear();
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        string? startupProjectPath = null;

        foreach (var projRef in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = Path.GetFullPath(Path.Combine(solutionDir, projRef.RelativePath));
            if (!File.Exists(projectPath))
                continue;

            try
            {
                var project = await LoadProjectAsync(projectPath, cancellationToken).ConfigureAwait(false);
                _loadedProjects.Add(project);

                if (startupProjectPath is null && project.IsExecutable)
                    startupProjectPath = projectPath;
            }
            catch (Exception)
            {
                // Skip projects that can't be parsed
            }
        }

        var finalSolution = solution with { StartupProjectPath = startupProjectPath };
        _currentSolution = finalSolution;
        SolutionLoaded?.Invoke(this, finalSolution);
        return finalSolution;
    }

    public Task<ProjectModel> LoadProjectAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        if (!File.Exists(projectPath))
            throw new FileNotFoundException("Project file not found.", projectPath);

        var project = ParseCsproj(projectPath);
        return Task.FromResult(project);
    }

    public Task CloseSolutionAsync(CancellationToken cancellationToken = default)
    {
        _currentSolution = null;
        _loadedProjects.Clear();
        SolutionClosed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string?> DetectSolutionFileAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
            return Task.FromResult<string?>(null);

        // Prefer .slnx over .sln
        var slnxFiles = Directory.GetFiles(directoryPath, "*.slnx", SearchOption.TopDirectoryOnly);
        if (slnxFiles.Length > 0)
            return Task.FromResult<string?>(slnxFiles[0]);

        var slnFiles = Directory.GetFiles(directoryPath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length > 0)
            return Task.FromResult<string?>(slnFiles[0]);

        return Task.FromResult<string?>(null);
    }

    public ProjectModel? GetStartupProject()
    {
        return _loadedProjects.FirstOrDefault(p => p.IsExecutable);
    }

    internal static async Task<SolutionModel> ParseSlnxAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var doc = XDocument.Parse(content);
        var root = doc.Root ?? throw new InvalidDataException("Invalid .slnx file: no root element.");

        var projects = new List<ProjectReference>();
        foreach (var projElement in root.Elements("Project"))
        {
            var pathAttr = projElement.Attribute("Path")?.Value;
            if (string.IsNullOrWhiteSpace(pathAttr))
                continue;

            // Normalize backslashes to forward slashes for cross-platform Path operations
            var normalizedPath = pathAttr.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(normalizedPath);
            projects.Add(new ProjectReference
            {
                Name = name,
                RelativePath = pathAttr.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar),
                ProjectGuid = Guid.NewGuid(),
                TypeGuid = Guid.Empty
            });
        }

        return new SolutionModel
        {
            FilePath = path,
            Name = Path.GetFileNameWithoutExtension(path),
            Format = SolutionFormat.Slnx,
            Projects = projects
        };
    }

    internal static async Task<SolutionModel> ParseSlnAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var projects = new List<ProjectReference>();

        foreach (var match in SlnProjectRegex().Matches(content).Cast<Match>())
        {
            var typeGuid = Guid.TryParse(match.Groups[1].Value, out var tg) ? tg : Guid.Empty;
            var name = match.Groups[2].Value;
            var relativePath = match.Groups[3].Value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var projectGuid = Guid.TryParse(match.Groups[4].Value, out var pg) ? pg : Guid.NewGuid();

            // Skip solution folders (type GUID {2150E333-8FDC-42A3-9474-1A3956D46DE8})
            if (typeGuid == Guid.Parse("2150E333-8FDC-42A3-9474-1A3956D46DE8"))
                continue;

            projects.Add(new ProjectReference
            {
                Name = name,
                RelativePath = relativePath,
                ProjectGuid = projectGuid,
                TypeGuid = typeGuid
            });
        }

        return new SolutionModel
        {
            FilePath = path,
            Name = Path.GetFileNameWithoutExtension(path),
            Format = SolutionFormat.Sln,
            Projects = projects
        };
    }

    internal static ProjectModel ParseCsproj(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidDataException($"Invalid project file: {path}");

        var sdk = root.Attribute("Sdk")?.Value ?? "Microsoft.NET.Sdk";

        // Find properties across all PropertyGroup elements
        string? targetFramework = null;
        string? outputType = null;
        string? rootNamespace = null;
        string? assemblyName = null;

        foreach (var pg in root.Elements("PropertyGroup"))
        {
            targetFramework ??= pg.Element("TargetFramework")?.Value;
            // Also check TargetFrameworks (plural) and take first
            targetFramework ??= pg.Element("TargetFrameworks")?.Value?.Split(';').FirstOrDefault();
            outputType ??= pg.Element("OutputType")?.Value;
            rootNamespace ??= pg.Element("RootNamespace")?.Value;
            assemblyName ??= pg.Element("AssemblyName")?.Value;
        }

        var packageRefs = root.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();

        var projectRefs = root.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .Cast<string>()
            .ToList();

        return new ProjectModel
        {
            FilePath = path,
            Name = Path.GetFileNameWithoutExtension(path),
            Sdk = sdk,
            TargetFramework = targetFramework ?? "unknown",
            OutputType = outputType,
            RootNamespace = rootNamespace,
            AssemblyName = assemblyName,
            PackageReferences = packageRefs,
            ProjectReferences = projectRefs
        };
    }

    // Pattern: Project("{TypeGuid}") = "Name", "Path", "{ProjectGuid}"
    [GeneratedRegex(@"Project\(""\{([^}]+)\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""")]
    private static partial Regex SlnProjectRegex();
}
