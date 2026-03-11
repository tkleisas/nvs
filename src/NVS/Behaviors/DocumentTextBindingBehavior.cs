using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Search;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Behaviors;

public class DocumentTextBindingBehavior : Behavior<TextEditor>
{
    private TextEditor? _textEditor;
    private SearchPanel? _searchPanel;
    private DiagnosticBackgroundRenderer? _diagnosticRenderer;
    private CompletionWindow? _completionWindow;
    private bool _updating;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, string>(
            nameof(Text), defaultValue: string.Empty);

    public static readonly StyledProperty<int> LineProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, int>(
            nameof(Line), defaultValue: 1);

    public static readonly StyledProperty<int> ColumnProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, int>(
            nameof(Column), defaultValue: 1);

    public static readonly StyledProperty<ICommand?> UndoCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(UndoCommand));

    public static readonly StyledProperty<ICommand?> RedoCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(RedoCommand));

    public static readonly StyledProperty<ICommand?> OpenSearchCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(OpenSearchCommand));

    public static readonly StyledProperty<IReadOnlyList<Diagnostic>?> DiagnosticsProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, IReadOnlyList<Diagnostic>?>(nameof(Diagnostics));

    public static readonly StyledProperty<ICommand?> GoToDefinitionCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(GoToDefinitionCommand));

    public static readonly StyledProperty<ICommand?> RequestCompletionCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(RequestCompletionCommand));

    public static readonly StyledProperty<IReadOnlyList<CompletionItem>?> CompletionResultsProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, IReadOnlyList<CompletionItem>?>(nameof(CompletionResults));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public int Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    public ICommand? UndoCommand
    {
        get => GetValue(UndoCommandProperty);
        set => SetValue(UndoCommandProperty, value);
    }

    public ICommand? RedoCommand
    {
        get => GetValue(RedoCommandProperty);
        set => SetValue(RedoCommandProperty, value);
    }

    public ICommand? OpenSearchCommand
    {
        get => GetValue(OpenSearchCommandProperty);
        set => SetValue(OpenSearchCommandProperty, value);
    }

    public IReadOnlyList<Diagnostic>? Diagnostics
    {
        get => GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    public ICommand? GoToDefinitionCommand
    {
        get => GetValue(GoToDefinitionCommandProperty);
        set => SetValue(GoToDefinitionCommandProperty, value);
    }

    public ICommand? RequestCompletionCommand
    {
        get => GetValue(RequestCompletionCommandProperty);
        set => SetValue(RequestCompletionCommandProperty, value);
    }

    public IReadOnlyList<CompletionItem>? CompletionResults
    {
        get => GetValue(CompletionResultsProperty);
        set => SetValue(CompletionResultsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is TextEditor textEditor)
        {
            _textEditor = textEditor;
            _textEditor.TextChanged += OnEditorTextChanged;
            _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            _textEditor.KeyDown += OnKeyDown;
            UpdateCaretPosition();

            _searchPanel = SearchPanel.Install(_textEditor);

            // Install diagnostic squiggly renderer
            _diagnosticRenderer = new DiagnosticBackgroundRenderer();
            _diagnosticRenderer.SetDocument(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);

            UndoCommand = new RelayCommand(
                () => _textEditor?.Undo(),
                () => _textEditor?.Document.UndoStack.CanUndo ?? false);
            RedoCommand = new RelayCommand(
                () => _textEditor?.Redo(),
                () => _textEditor?.Document.UndoStack.CanRedo ?? false);
            OpenSearchCommand = new RelayCommand(
                () => _searchPanel?.Open());
        }
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        if (_textEditor != null)
        {
            _textEditor.TextChanged -= OnEditorTextChanged;
            _textEditor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            _textEditor.KeyDown -= OnKeyDown;

            if (_diagnosticRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_diagnosticRenderer);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && !_updating)
        {
            _updating = true;
            var text = change.GetNewValue<string>() ?? "";
            if (_textEditor?.Document != null)
            {
                var caretOffset = _textEditor.CaretOffset;
                _textEditor.Document.Text = text;
                if (caretOffset <= text.Length)
                    _textEditor.CaretOffset = caretOffset;
            }
            _updating = false;
        }
        else if (change.Property == DiagnosticsProperty)
        {
            var diagnostics = change.GetNewValue<IReadOnlyList<Diagnostic>?>() ?? [];
            _diagnosticRenderer?.UpdateDiagnostics(diagnostics);
            _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        }
        else if (change.Property == CompletionResultsProperty)
        {
            var items = change.GetNewValue<IReadOnlyList<CompletionItem>?>();
            if (items is { Count: > 0 })
                ShowCompletionWindow(items);
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updating) return;
        _updating = true;
        if (_textEditor?.Document != null)
            Text = _textEditor.Document.Text;
        _updating = false;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        UpdateCaretPosition();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space → request code completion
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            RequestCompletionCommand?.Execute(null);
        }
        // F12 → go to definition
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            GoToDefinitionCommand?.Execute(null);
        }
    }

    /// <summary>
    /// Shows the AvaloniaEdit completion window with the given items.
    /// Called by the ViewModel after LSP returns completions.
    /// </summary>
    public void ShowCompletionWindow(IReadOnlyList<CompletionItem> items)
    {
        if (_textEditor is null || items.Count == 0)
            return;

        _completionWindow?.Close();

        _completionWindow = new CompletionWindow(_textEditor.TextArea);
        foreach (var item in items)
        {
            _completionWindow.CompletionList.CompletionData.Add(new LspCompletionData(item));
        }

        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    private void UpdateCaretPosition()
    {
        if (_textEditor != null)
        {
            Line = _textEditor.TextArea.Caret.Line;
            Column = _textEditor.TextArea.Caret.Column;
        }
    }
}
