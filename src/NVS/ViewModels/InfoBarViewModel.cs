using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;

namespace NVS.ViewModels;

public enum InfoBarSeverity
{
    Info,
    Warning,
    Error,
}

public partial class InfoBarViewModel : INotifyPropertyChanged
{
    private bool _isVisible = true;

    public InfoBarViewModel(string message, InfoBarSeverity severity, string? actionLabel = null, Action? action = null)
    {
        Message = message;
        Severity = severity;
        ActionLabel = actionLabel;
        Action = action;
    }

    public string Message { get; }
    public InfoBarSeverity Severity { get; }
    public string? ActionLabel { get; }
    public Action? Action { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public string BackgroundColor => Severity switch
    {
        InfoBarSeverity.Warning => "#CC6600",
        InfoBarSeverity.Error => "#CC0000",
        _ => "#007ACC",
    };

    public string BackgroundResourceKey => Severity switch
    {
        InfoBarSeverity.Warning => "InfoBarWarningBackgroundBrush",
        InfoBarSeverity.Error => "InfoBarErrorBackgroundBrush",
        _ => "InfoBarInfoBackgroundBrush",
    };

    public string IconGlyph => Severity switch
    {
        InfoBarSeverity.Warning => "⚠",
        InfoBarSeverity.Error => "✖",
        _ => "ℹ",
    };

    [RelayCommand]
    private void Dismiss()
    {
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ExecuteAction()
    {
        Action?.Invoke();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Dismissed;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
