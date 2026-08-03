using Avalonia;
using AvaloniaEdit;
using NVS.Core.Enums;
using NVS.Highlighting;

namespace NVS.Behaviors;

public static class TextEditorSyntaxHighlighting
{
    public static readonly AttachedProperty<Language> LanguageProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, Language>("Language", typeof(TextEditorSyntaxHighlighting));

    // Editors with an assigned language, re-highlighted when the theme changes.
    private static readonly HashSet<TextEditor> ActiveEditors = new();

    static TextEditorSyntaxHighlighting()
    {
        LanguageProperty.Changed.AddClassHandler<TextEditor>(OnLanguageChanged);
        SyntaxHighlightingLoader.CacheInvalidated += OnCacheInvalidated;
    }

    public static Language GetLanguage(TextEditor editor) => editor.GetValue(LanguageProperty);
    public static void SetLanguage(TextEditor editor, Language value) => editor.SetValue(LanguageProperty, value);

    private static void OnLanguageChanged(TextEditor editor, AvaloniaPropertyChangedEventArgs args)
    {
        if (args is AvaloniaPropertyChangedEventArgs<Language> languageArgs)
        {
            var language = languageArgs.NewValue.Value;
            var highlighting = SyntaxHighlightingLoader.GetHighlighting(language);
            editor.SyntaxHighlighting = highlighting;

            if (highlighting is not null)
            {
                if (ActiveEditors.Add(editor))
                {
                    editor.DetachedFromVisualTree += OnEditorDetached;
                }
            }
        }
    }

    private static void OnCacheInvalidated(object? sender, EventArgs e)
    {
        foreach (var editor in ActiveEditors)
        {
            editor.SyntaxHighlighting = null;
            editor.SyntaxHighlighting = SyntaxHighlightingLoader.GetHighlighting(GetLanguage(editor));
        }
    }

    private static void OnEditorDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextEditor editor)
        {
            editor.DetachedFromVisualTree -= OnEditorDetached;
            ActiveEditors.Remove(editor);
        }
    }
}
