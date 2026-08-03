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
            editor.ConfirmCloseDirtyDocument = ConfirmCloseDirtyDocumentAsync;
            UpdateSplitLayout(editor.IsSplitActive, editor.IsSplitVertical);
        }
    }

    private async Task<DirtyCloseChoice> ConfirmCloseDirtyDocumentAsync(DocumentViewModel docVm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return DirtyCloseChoice.Cancel;
        }

        return await UnsavedChangesDialog.ShowAsync(owner, docVm.Document.Name);
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.IsSplitActive) or nameof(EditorViewModel.IsSplitVertical)
            && sender is EditorViewModel editor)
        {
            UpdateSplitLayout(editor.IsSplitActive, editor.IsSplitVertical);
        }
    }

    private void UpdateSplitLayout(bool isSplitActive, bool isVerticalDivider)
    {
        if (Content is not Grid grid || grid.ColumnDefinitions.Count < 3 || grid.RowDefinitions.Count < 3)
        {
            return;
        }

        var splitter = this.FindControl<GridSplitter>("Splitter");
        var splitPane = this.FindControl<TabControl>("SplitPane");
        if (splitter is null || splitPane is null)
        {
            return;
        }

        if (!isSplitActive)
        {
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(0);
            return;
        }

        if (isVerticalDivider)
        {
            // Split Right: panes side by side, vertical divider line
            grid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
            grid.RowDefinitions[2].Height = new GridLength(0);

            Grid.SetColumn(splitPane, 2);
            Grid.SetRow(splitPane, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetRow(splitter, 0);
            splitter.Width = 4;
            splitter.Height = double.NaN;
            splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            splitter.ResizeDirection = GridResizeDirection.Columns;
        }
        else
        {
            // Split Down: panes stacked, horizontal divider line
            grid.ColumnDefinitions[2].Width = new GridLength(0);
            grid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(splitPane, 0);
            Grid.SetRow(splitPane, 2);
            Grid.SetColumn(splitter, 0);
            Grid.SetRow(splitter, 1);
            splitter.Width = double.NaN;
            splitter.Height = 4;
            splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            splitter.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            splitter.ResizeDirection = GridResizeDirection.Rows;
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

        if (docVm.QuickFixCommand is not CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<LspRequestContext?> asyncCmd
            || !asyncCmd.CanExecute(ctx))
        {
            return;
        }

        // Await the code-action request instead of guessing a fixed delay.
        await asyncCmd.ExecuteAsync(ctx);

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
