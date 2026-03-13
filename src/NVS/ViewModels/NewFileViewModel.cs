using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Template;

namespace NVS.ViewModels;

public sealed partial class NewFileViewModel : ObservableObject
{
    private readonly ITemplateService _templateService;

    private FileTemplate? _selectedTemplate;
    private string _fileName = "NewClass";
    private string _directory = "";
    private string _namespaceName = "";
    private string? _errorMessage;

    public NewFileViewModel(ITemplateService templateService, string directory, string? projectRoot = null)
    {
        _templateService = templateService;
        _directory = directory;
        _namespaceName = TemplateService.InferNamespace(directory, projectRoot);

        Templates = new ObservableCollection<FileTemplate>(templateService.GetFileTemplates());
        if (Templates.Count > 0)
        {
            SelectedTemplate = Templates[0];
        }
    }

    public ObservableCollection<FileTemplate> Templates { get; }

    public FileTemplate? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            if (SetProperty(ref _selectedTemplate, value))
            {
                if (value is not null)
                    FileName = value.DefaultFileName;
                UpdatePreview();
                CreateFileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetProperty(ref _fileName, value))
            {
                ErrorMessage = null;
                UpdatePreview();
                CreateFileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Directory
    {
        get => _directory;
        set => SetProperty(ref _directory, value);
    }

    public string NamespaceName
    {
        get => _namespaceName;
        set
        {
            if (SetProperty(ref _namespaceName, value))
                UpdatePreview();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private string _preview = "";
    public string Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public string? CreatedFilePath { get; private set; }

    public event EventHandler? RequestClose;

    [RelayCommand(CanExecute = nameof(CanCreateFile))]
    private async Task CreateFileAsync()
    {
        ErrorMessage = null;

        try
        {
            var validation = ValidateInputs();
            if (validation is not null)
            {
                ErrorMessage = validation;
                return;
            }

            CreatedFilePath = await _templateService.CreateFileFromTemplateAsync(
                SelectedTemplate!.Id,
                FileName,
                Directory,
                NamespaceName);

            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create file: {ex.Message}";
        }
    }

    private bool CanCreateFile() =>
        SelectedTemplate is not null
        && !string.IsNullOrWhiteSpace(FileName)
        && !string.IsNullOrWhiteSpace(NamespaceName);

    private string? ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(FileName))
            return "File name is required.";

        var invalidChars = Path.GetInvalidFileNameChars();
        if (FileName.Any(c => invalidChars.Contains(c)))
            return "File name contains invalid characters.";

        if (string.IsNullOrWhiteSpace(NamespaceName))
            return "Namespace is required.";

        return null;
    }

    private void UpdatePreview()
    {
        if (SelectedTemplate is null || string.IsNullOrWhiteSpace(FileName) || string.IsNullOrWhiteSpace(NamespaceName))
        {
            Preview = "";
            return;
        }

        Preview = SelectedTemplate.ContentTemplate
            .Replace("{{Namespace}}", NamespaceName)
            .Replace("{{Name}}", FileName);
    }
}
