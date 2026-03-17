using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editor)
        {
            editor.PropertyChanged += OnEditorPropertyChanged;
            UpdateSplitColumnWidth(editor.IsSplitActive);
        }
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsSplitActive) && sender is EditorViewModel editor)
        {
            UpdateSplitColumnWidth(editor.IsSplitActive);
        }
    }

    private void UpdateSplitColumnWidth(bool isSplitActive)
    {
        if (this.Content is Grid grid && grid.ColumnDefinitions.Count >= 3)
        {
            grid.ColumnDefinitions[2].Width = isSplitActive
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
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
        var isSplitActive = false;
        if (menu.Parent is TextEditor { DataContext: DocumentViewModel docVm })
        {
            isSql = docVm.Language == Language.Sql;
        }
        if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editorVm)
        {
            isSplitActive = editorVm.IsSplitActive;
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
                case MenuItem { Tag: "CloseSplit" } closeSplit:
                    closeSplit.IsVisible = isSplitActive;
                    break;
            }
        }
    }

    private async void OnQuickFixClick(object? sender, RoutedEventArgs e)
    {
        var editor = GetEditorFromMenuItem(sender);
        if (editor?.DataContext is not DocumentViewModel docVm) return;

        // Trigger code actions via the command
        var ctx = new LspRequestContext(
            docVm.CursorLine,
            docVm.CursorColumn,
            editor.Text ?? "");

        if (docVm.QuickFixCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(ctx))
        {
            cmd.Execute(ctx);

            // Wait briefly for the async command to complete
            await Task.Delay(500);

            var actions = docVm.LastCodeActions;
            if (actions is not { Count: > 0 }) return;

            // Build a popup menu with the available code actions
            var actionMenu = new ContextMenu();
            foreach (var action in actions)
            {
                var menuItem = new MenuItem
                {
                    Header = FormatCodeActionHeader(action),
                };

                var capturedAction = action;
                menuItem.Click += async (_, _) =>
                {
                    if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editor2)
                    {
                        await editor2.ApplyCodeActionAsync(capturedAction);
                    }
                };

                actionMenu.Items.Add(menuItem);
            }

            actionMenu.Open(editor);
        }
    }

    private static string FormatCodeActionHeader(CodeAction action)
    {
        var prefix = action.Kind switch
        {
            "quickfix" => "🔧 ",
            var k when k?.StartsWith("refactor") == true => "✏️ ",
            var k when k?.StartsWith("source") == true => "📄 ",
            _ => "",
        };

        var preferred = action.IsPreferred ? " ★" : "";
        return $"{prefix}{action.Title}{preferred}";
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

    private void OnSplitRightClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editor)
            editor.SplitRightCommand.Execute(null);
    }

    private void OnSplitDownClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editor)
            editor.SplitDownCommand.Execute(null);
    }

    private void OnCloseSplitClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorDocumentViewModel editorDocVm && editorDocVm.Main.Editor is { } editor)
            editor.CloseSplitCommand.Execute(null);
    }
}
