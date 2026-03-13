using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface ITemplateService
{
    /// <summary>
    /// Gets the built-in project templates (dotnet new short names).
    /// </summary>
    IReadOnlyList<ProjectTemplate> GetProjectTemplates();

    /// <summary>
    /// Gets the built-in file templates (class, interface, record, etc.).
    /// </summary>
    IReadOnlyList<FileTemplate> GetFileTemplates();

    /// <summary>
    /// Creates a new project using dotnet new.
    /// </summary>
    /// <param name="templateShortName">The dotnet new short name (e.g. "console").</param>
    /// <param name="projectName">The project name.</param>
    /// <param name="outputDirectory">The directory where the project will be created.</param>
    /// <param name="framework">Optional target framework (e.g. "net10.0").</param>
    /// <param name="createSolution">Whether to also create a solution file and add the project. Default true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the created project directory.</returns>
    Task<string> CreateProjectAsync(
        string templateShortName,
        string projectName,
        string outputDirectory,
        string? framework = null,
        bool createSolution = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new file from a file template.
    /// </summary>
    /// <param name="templateId">The file template ID.</param>
    /// <param name="fileName">The file name (without extension).</param>
    /// <param name="directory">The directory where the file will be created.</param>
    /// <param name="namespaceName">The namespace to use in the file content.</param>
    /// <returns>The full path to the created file.</returns>
    Task<string> CreateFileFromTemplateAsync(
        string templateId,
        string fileName,
        string directory,
        string namespaceName,
        CancellationToken cancellationToken = default);
}
