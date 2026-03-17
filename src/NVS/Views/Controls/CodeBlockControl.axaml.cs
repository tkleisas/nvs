using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace NVS.Views.Controls;

public partial class CodeBlockControl : UserControl
{
    private string _code = string.Empty;

    /// <summary>Raised when the user clicks Apply to insert code into the editor.</summary>
    public event EventHandler<string>? ApplyRequested;

    public CodeBlockControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void SetContent(string code, string? language)
    {
        _code = code;

        var codeText = this.FindControl<SelectableTextBlock>("CodeText");
        var languageLabel = this.FindControl<TextBlock>("LanguageLabel");

        if (codeText is not null)
            codeText.Text = code;

        if (languageLabel is not null)
            languageLabel.Text = string.IsNullOrEmpty(language) ? "code" : language;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(_code);

            var btn = this.FindControl<Button>("CopyButton");
            if (btn is not null)
            {
                btn.Content = "✓";
                await Task.Delay(1000);
                btn.Content = "📋";
            }
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ApplyRequested?.Invoke(this, _code);
    }
}
