using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private CurrentLineHighlightRenderer? _currentLineRenderer;
    private BreakpointMargin? _breakpointMargin;
    private CompletionWindow? _completionWindow;
    private CancellationTokenSource? _autoCompleteCts;
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

    public static readonly StyledProperty<IReadOnlyList<(int Line, bool Verified)>?> BreakpointsProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, IReadOnlyList<(int Line, bool Verified)>?>(nameof(Breakpoints));

    public static readonly StyledProperty<ICommand?> ToggleBreakpointCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(ToggleBreakpointCommand));

    public static readonly StyledProperty<int?> DebugCurrentLineProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, int?>(nameof(DebugCurrentLine));

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

    public IReadOnlyList<(int Line, bool Verified)>? Breakpoints
    {
        get => GetValue(BreakpointsProperty);
        set => SetValue(BreakpointsProperty, value);
    }

    public ICommand? ToggleBreakpointCommand
    {
        get => GetValue(ToggleBreakpointCommandProperty);
        set => SetValue(ToggleBreakpointCommandProperty, value);
    }

    public int? DebugCurrentLine
    {
        get => GetValue(DebugCurrentLineProperty);
        set => SetValue(DebugCurrentLineProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is TextEditor textEditor)
        {
            _textEditor = textEditor;
            _textEditor.TextChanged += OnEditorTextChanged;
            _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            _textEditor.TextArea.TextEntered += OnTextEntered;
            _textEditor.KeyDown += OnKeyDown;
            UpdateCaretPosition();

            _searchPanel = SearchPanel.Install(_textEditor);

            // Install diagnostic squiggly renderer
            _diagnosticRenderer = new DiagnosticBackgroundRenderer();
            _diagnosticRenderer.SetDocument(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);

            // Install debug current line highlight renderer
            _currentLineRenderer = new CurrentLineHighlightRenderer(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_currentLineRenderer);

            // Install breakpoint margin (left gutter)
            _breakpointMargin = new BreakpointMargin();
            _breakpointMargin.BreakpointToggled += OnBreakpointToggled;
            _textEditor.TextArea.LeftMargins.Insert(0, _breakpointMargin);

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
            _textEditor.TextArea.TextEntered -= OnTextEntered;
            _textEditor.KeyDown -= OnKeyDown;
            _autoCompleteCts?.Cancel();
            _autoCompleteCts?.Dispose();

            if (_diagnosticRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_diagnosticRenderer);

            if (_currentLineRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_currentLineRenderer);

            if (_breakpointMargin != null)
            {
                _breakpointMargin.BreakpointToggled -= OnBreakpointToggled;
                _textEditor.TextArea.LeftMargins.Remove(_breakpointMargin);
            }
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
        else if (change.Property == BreakpointsProperty)
        {
            var breakpoints = change.GetNewValue<IReadOnlyList<(int Line, bool Verified)>?>() ?? [];
            _breakpointMargin?.UpdateBreakpoints(breakpoints);
        }
        else if (change.Property == DebugCurrentLineProperty)
        {
            var line = change.GetNewValue<int?>();
            _currentLineRenderer?.SetCurrentLine(line);
            _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);

            // Scroll to the debug line
            if (line.HasValue && _textEditor?.Document is not null && line.Value <= _textEditor.Document.LineCount)
            {
                _textEditor.TextArea.Caret.Line = line.Value;
                _textEditor.TextArea.Caret.Column = 1;
                _textEditor.ScrollToLine(line.Value);
            }
        }
        else if (change.Property == LineProperty && !_updating)
        {
            var line = change.GetNewValue<int>();
            if (_textEditor?.Document is not null && line > 0 && line <= _textEditor.Document.LineCount)
            {
                _textEditor.TextArea.Caret.Line = line;
                _textEditor.ScrollToLine(line);
            }
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
        // F9 → toggle breakpoint at current line
        else if (e.Key == Key.F9 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (_textEditor is not null)
            {
                var line = _textEditor.TextArea.Caret.Line;
                ToggleBreakpointCommand?.Execute(line);
            }
        }
    }

    /// <summary>
    /// Auto-triggers completion after typing trigger characters (., (, &lt;)
    /// or after a short delay when typing identifier characters.
    /// </summary>
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completionWindow is not null || RequestCompletionCommand is null)
            return;

        var text = e.Text;
        if (string.IsNullOrEmpty(text))
            return;

        var ch = text[0];

        // Immediate trigger characters (like VS/Rider)
        if (ch is '.' or '(' or '<' or ':')
        {
            RequestCompletionCommand.Execute(null);
            return;
        }

        // Debounced trigger for identifier characters
        if (char.IsLetterOrDigit(ch) || ch == '_')
        {
            _autoCompleteCts?.Cancel();
            _autoCompleteCts?.Dispose();
            _autoCompleteCts = new CancellationTokenSource();
            var token = _autoCompleteCts.Token;

            _ = Task.Delay(300, token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_completionWindow is null)
                            RequestCompletionCommand?.Execute(null);
                    });
                }
            }, TaskScheduler.Default);
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

    private void OnBreakpointToggled(object? sender, int line)
    {
        ToggleBreakpointCommand?.Execute(line);
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
