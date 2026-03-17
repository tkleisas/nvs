using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using NVS.Core.LLM;
using NVS.ViewModels.Dock;

namespace NVS.Views.Controls;

/// <summary>
/// Renders chat message content as a mix of text and code blocks.
/// Parses markdown code fences and displays code in CodeBlockControls.
/// </summary>
public sealed class ChatContentPresenter : StackPanel
{
    public static readonly StyledProperty<string?> ContentTextProperty =
        AvaloniaProperty.Register<ChatContentPresenter, string?>(nameof(ContentText));

    public string? ContentText
    {
        get => GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    static ChatContentPresenter()
    {
        ContentTextProperty.Changed.AddClassHandler<ChatContentPresenter>(
            (presenter, _) => presenter.RenderContent());
    }

    private void RenderContent()
    {
        Children.Clear();

        var text = ContentText;
        if (string.IsNullOrEmpty(text))
            return;

        var segments = MarkdownCodeBlockParser.Parse(text);

        foreach (var segment in segments)
        {
            if (segment.Type == SegmentType.Code)
            {
                var codeBlock = new CodeBlockControl();
                codeBlock.SetContent(segment.Content, segment.Language);
                codeBlock.ApplyRequested += OnApplyCodeRequested;
                Children.Add(codeBlock);
            }
            else
            {
                var textBlock = new SelectableTextBlock
                {
                    Text = segment.Content,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#D4D4D4"))
                };
                Children.Add(textBlock);
            }
        }
    }

    private void OnApplyCodeRequested(object? sender, string code)
    {
        // Walk up the visual tree to find the LlmChatView and its ViewModel
        var chatView = this.FindAncestorOfType<UserControl>();
        if (chatView?.DataContext is LlmChatToolViewModel chatVm)
        {
            var activeDoc = chatVm.Main.Editor?.ActiveDocument;
            if (activeDoc is not null)
            {
                // Insert at cursor or replace selection
                activeDoc.Text = code;
            }
        }
    }
}
