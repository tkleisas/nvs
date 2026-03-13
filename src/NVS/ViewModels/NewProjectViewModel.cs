using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

public sealed partial class NewProjectViewModel : ObservableObject
{
    private readonly ITemplateService _templateService;

    private ProjectTemplate? _selectedTemplate;
    private string _projectName = "MyApp";
    private string _location = "";
    private string _selectedFramework = "net10.0";
    private string? _errorMessage;
    private bool _isCreating;

    /// <summary>
    /// Whether to also create a solution file when creating the project. Default true.
    /// Set to false when adding a project to an existing solution.
    /// </summary>
    public bool CreateSolution { get; set; } = true;

    public NewProjectViewModel(ITemplateService templateService)
    {
        _templateService = templateService;
        Templates = new ObservableCollection<ProjectTemplate>(templateService.GetProjectTemplates());
        if (Templates.Count > 0)
        {
            SelectedTemplate = Templates[0];
        }

        _location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "source", "repos");
    }

    public ObservableCollection<ProjectTemplate> Templates { get; }

    public ProjectTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                UpdateFrameworks();
                CreateProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (SetProperty(ref _projectName, value))
            {
                ErrorMessage = null;
                CreateProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Location
    {
        get => _location;
        set
        {
            if (SetProperty(ref _location, value))
            {
                ErrorMessage = null;
                CreateProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectedFramework
    {
        get => _selectedFramework;
        set => SetProperty(ref _selectedFramework, value);
    }

    public ObservableCollection<string> AvailableFrameworks { get; } = new() { "net10.0", "net9.0", "net8.0" };

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        set => SetProperty(ref _isCreating, value);
    }

    /// <summary>
    /// Set by the dialog when creation succeeds — the full path to the created project directory.
    /// </summary>
    public string? CreatedProjectPath { get; private set; }

    /// <summary>
    /// Raised when the dialog should close (after successful creation).
    /// </summary>
    public event EventHandler? RequestClose;

    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateProjectAsync()
    {
        ErrorMessage = null;
        IsCreating = true;

        try
        {
            var validation = ValidateInputs();
            if (validation is not null)
            {
                ErrorMessage = validation;
                return;
            }

            var projectDir = Path.Combine(Location, ProjectName);
            if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
            {
                ErrorMessage = $"Directory '{ProjectName}' already exists and is not empty.";
                return;
            }

            CreatedProjectPath = await _templateService.CreateProjectAsync(
                SelectedTemplate!.ShortName,
                ProjectName,
                Location,
                SelectedFramework,
                CreateSolution);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create project: {ex.Message}";
        }
        finally
        {
            IsCreating = false;
        }
    }

    private bool CanCreateProject() =>
        SelectedTemplate is not null
        && !string.IsNullOrWhiteSpace(ProjectName)
        && !string.IsNullOrWhiteSpace(Location)
        && !IsCreating;

    private string? ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
            return "Project name is required.";

        var invalidChars = Path.GetInvalidFileNameChars();
        if (ProjectName.Any(c => invalidChars.Contains(c)))
            return "Project name contains invalid characters.";

        if (string.IsNullOrWhiteSpace(Location))
            return "Location is required.";

        return null;
    }

    private void UpdateFrameworks()
    {
        AvailableFrameworks.Clear();
        if (SelectedTemplate is not null)
        {
            foreach (var fw in SelectedTemplate.Frameworks)
                AvailableFrameworks.Add(fw);

            if (AvailableFrameworks.Count > 0 && !AvailableFrameworks.Contains(SelectedFramework))
                SelectedFramework = AvailableFrameworks[0];
        }
    }
}
