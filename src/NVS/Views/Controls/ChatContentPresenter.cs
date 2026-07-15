using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using NVS.Core.LLM;
using NVS.ViewModels.Dock;

namespace NVS.Views.Controls;

/// <summary>
/// Renders chat message content as a mix of markdown text and code blocks.
/// Code fences become CodeBlockControls; the remaining text renders with
/// headings, lists, quotes, and inline bold/italic/code styling, all selectable.
/// </summary>
public sealed class ChatContentPresenter : StackPanel
{
    private static readonly SolidColorBrush TextBrush = new(Color.Parse("#D4D4D4"));
    private static readonly SolidColorBrush MutedBrush = new(Color.Parse("#9A9A9A"));
    private static readonly SolidColorBrush RuleBrush = new(Color.Parse("#3F3F46"));
    private static readonly SolidColorBrush InlineCodeForeground = new(Color.Parse("#CE9178"));
    private static readonly SolidColorBrush InlineCodeBackground = new(Color.Parse("#1E1E1E"));
    private static readonly SolidColorBrush LinkBrush = new(Color.Parse("#569CD6"));
    private static readonly FontFamily MonoFont = new("Cascadia Code,Consolas,Menlo,monospace");
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
                foreach (var block in MarkdownTextParser.Parse(segment.Content))
                {
                    Children.Add(CreateBlockControl(block));
                }
            }
        }
    }

    private static Control CreateBlockControl(MarkdownBlock block)
    {
        if (block.Type == MarkdownBlockType.HorizontalRule)
        {
            return new Border
            {
                Height = 1,
                Background = RuleBrush,
                Margin = new Thickness(0, 6)
            };
        }

        var textBlock = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = block.Type == MarkdownBlockType.Quote ? MutedBrush : TextBrush
        };

        switch (block.Type)
        {
            case MarkdownBlockType.Heading:
                textBlock.FontWeight = FontWeight.Bold;
                textBlock.FontSize = block.HeadingLevel switch
                {
                    1 => 17,
                    2 => 15,
                    3 => 14,
                    _ => 13
                };
                textBlock.Margin = new Thickness(0, 6, 0, 2);
                break;

            case MarkdownBlockType.ListItem:
                textBlock.Margin = new Thickness(10 + block.IndentLevel * 14, 1, 0, 1);
                textBlock.Inlines!.Add(new Run($"{block.ListMarker} ") { Foreground = MutedBrush });
                break;
        }

        foreach (var span in block.Spans)
        {
            textBlock.Inlines!.Add(CreateRun(span));
        }

        if (block.Type == MarkdownBlockType.Quote)
        {
            return new Border
            {
                BorderBrush = RuleBrush,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(8, 1, 0, 1),
                Margin = new Thickness(0, 2),
                Child = textBlock
            };
        }

        return textBlock;
    }

    private static Run CreateRun(MarkdownSpan span)
    {
        var run = new Run(span.Text);

        if (span.Code)
        {
            run.FontFamily = MonoFont;
            run.Foreground = InlineCodeForeground;
            run.Background = InlineCodeBackground;
            return run;
        }

        if (span.Bold)
            run.FontWeight = FontWeight.Bold;
        if (span.Italic)
            run.FontStyle = FontStyle.Italic;
        if (span.Strikethrough)
            run.TextDecorations = TextDecorations.Strikethrough;
        if (span.LinkUrl is not null)
        {
            run.Foreground = LinkBrush;
            run.TextDecorations = TextDecorations.Underline;
        }

        return run;
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
