using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;

namespace NVS.Views.Dock;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
    }

    private static TextEditor? GetEditorFromMenuItem(object? sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { Parent: Control parent } })
        {
            return parent as TextEditor;
        }
        return null;
    }

    private void OnCutClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        editor?.Cut();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        editor?.Copy();
    }

    private void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        editor?.Paste();
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        editor?.SelectAll();
    }
}
