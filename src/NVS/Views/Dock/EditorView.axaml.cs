using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using NVS.Core.Enums;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

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

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var isSql = false;
        if (menu.Parent is TextEditor { DataContext: DocumentViewModel docVm })
        {
            isSql = docVm.Language == Language.Sql;
        }

        foreach (var item in menu.Items)
        {
            switch (item)
            {
                case MenuItem { Tag: "ExecuteSql" } mi:
                    mi.IsVisible = isSql;
                    break;
                case Separator { Tag: "SqlSeparator" } sep:
                    sep.IsVisible = isSql;
                    break;
            }
        }
    }

    private async void OnExecuteInDatabaseExplorerClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        if (editor is null) return;

        // Use selected text, or the full document text
        var sql = editor.SelectedText;
        if (string.IsNullOrWhiteSpace(sql))
            sql = editor.Text;

        if (string.IsNullOrWhiteSpace(sql)) return;

        // Walk up to find MainViewModel
        if (DataContext is EditorDocumentViewModel editorDocVm)
        {
            await editorDocVm.Main.ExecuteSqlInDatabaseExplorer(sql);
        }
    }
}
