using Avalonia;
using AvaloniaEdit;
using NVS.Core.Enums;
using NVS.Highlighting;

namespace NVS.Behaviors;

public static class TextEditorSyntaxHighlighting
{
    public static readonly AttachedProperty<Language> LanguageProperty =
        AvaloniaProperty.RegisterAttached<TextEditor, Language>("Language", typeof(TextEditorSyntaxHighlighting));

    static TextEditorSyntaxHighlighting()
    {
        LanguageProperty.Changed.AddClassHandler<TextEditor>(OnLanguageChanged);
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
        }
    }
}
