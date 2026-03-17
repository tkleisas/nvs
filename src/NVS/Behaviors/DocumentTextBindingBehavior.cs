using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using CommunityToolkit.Mvvm.Input;
using NVS.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels;

namespace NVS.Behaviors;

public class DocumentTextBindingBehavior : Behavior<TextEditor>
{
    private TextEditor? _textEditor;
    private SearchPanel? _searchPanel;
    private DiagnosticBackgroundRenderer? _diagnosticRenderer;
    private CurrentLineHighlightRenderer? _currentLineRenderer;
    private BracketHighlightRenderer? _bracketRenderer;
    private FoldingHelper? _foldingHelper;
    private MinimapControl? _minimapControl;
    private BreakpointMargin? _breakpointMargin;
    private MetricsGutterMargin? _metricsMargin;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _insightWindow;
    private CancellationTokenSource? _autoCompleteCts;
    private CancellationTokenSource? _hoverCts;
    private bool _debugHoverShowing;
    private string? _lastHoverWord;
    private bool _updating;
    private MultiCursorState? _multiCursorState;
    private MultiCursorRenderer? _multiCursorRenderer;
    private bool _applyingMultiCursorEdits;
    private IDisposable? _languagePropertySubscription;

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

    public static readonly StyledProperty<ICommand?> RequestSignatureHelpCommandProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, ICommand?>(nameof(RequestSignatureHelpCommand));

    public static readonly StyledProperty<SignatureHelp?> SignatureHelpResultProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, SignatureHelp?>(nameof(SignatureHelpResult));

    public static readonly StyledProperty<IReadOnlyList<MethodMetrics>?> FileMethodMetricsProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, IReadOnlyList<MethodMetrics>?>(nameof(FileMethodMetrics));

    public static readonly StyledProperty<Func<string, CancellationToken, Task<string?>>?> DebugEvaluateFuncProperty =
        AvaloniaProperty.Register<DocumentTextBindingBehavior, Func<string, CancellationToken, Task<string?>>?>(nameof(DebugEvaluateFunc));

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

    public ICommand? RequestSignatureHelpCommand
    {
        get => GetValue(RequestSignatureHelpCommandProperty);
        set => SetValue(RequestSignatureHelpCommandProperty, value);
    }

    public SignatureHelp? SignatureHelpResult
    {
        get => GetValue(SignatureHelpResultProperty);
        set => SetValue(SignatureHelpResultProperty, value);
    }

    public IReadOnlyList<MethodMetrics>? FileMethodMetrics
    {
        get => GetValue(FileMethodMetricsProperty);
        set => SetValue(FileMethodMetricsProperty, value);
    }

    public Func<string, CancellationToken, Task<string?>>? DebugEvaluateFunc
    {
        get => GetValue(DebugEvaluateFuncProperty);
        set => SetValue(DebugEvaluateFuncProperty, value);
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
            _textEditor.TextArea.TextView.PointerHover += OnTextViewPointerHover;
            _textEditor.TextArea.TextView.PointerHoverStopped += OnTextViewPointerHoverStopped;
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

            // Install bracket highlight renderer
            _bracketRenderer = new BracketHighlightRenderer();
            _bracketRenderer.SetDocument(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_bracketRenderer);

            // Install multi-cursor renderer
            _multiCursorState = new MultiCursorState();
            _multiCursorRenderer = new MultiCursorRenderer();
            _multiCursorRenderer.SetDocument(_textEditor.Document);
            _multiCursorRenderer.SetState(_multiCursorState);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_multiCursorRenderer);

            // Install code folding (language may not be bound yet, will update on change)
            var language = TextEditorSyntaxHighlighting.GetLanguage(_textEditor);
            _foldingHelper = new FoldingHelper(_textEditor, language);

            // Listen for Language attached property changes so folding updates when binding applies
            _languagePropertySubscription = TextEditorSyntaxHighlighting.LanguageProperty.Changed.Subscribe(args =>
            {
                if (args.Sender == _textEditor && _foldingHelper is not null)
                {
                    _foldingHelper.SetLanguage(args.NewValue.GetValueOrDefault());
                }
            });

            // Defer minimap wiring to AttachedToVisualTree so sibling controls exist
            _textEditor.AttachedToVisualTree += OnEditorAttachedToVisualTree;

            // Install breakpoint margin (left gutter)
            _breakpointMargin = new BreakpointMargin();
            _breakpointMargin.BreakpointToggled += OnBreakpointToggled;
            _textEditor.TextArea.LeftMargins.Insert(0, _breakpointMargin);

            // Install metrics gutter margin (right of breakpoints)
            _metricsMargin = new MetricsGutterMargin();
            _textEditor.TextArea.LeftMargins.Insert(1, _metricsMargin);

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
            _textEditor.TextArea.TextView.PointerHover -= OnTextViewPointerHover;
            _textEditor.TextArea.TextView.PointerHoverStopped -= OnTextViewPointerHoverStopped;
            _textEditor.KeyDown -= OnKeyDown;
            _textEditor.AttachedToVisualTree -= OnEditorAttachedToVisualTree;
            _autoCompleteCts?.Cancel();
            _autoCompleteCts?.Dispose();
            _hoverCts?.Cancel();
            _hoverCts?.Dispose();
            _lastHoverWord = null;
            CloseDebugHoverPopup();

            if (_diagnosticRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_diagnosticRenderer);

            if (_currentLineRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_currentLineRenderer);

            if (_bracketRenderer != null)
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_bracketRenderer);

            _foldingHelper?.Dispose();
            _languagePropertySubscription?.Dispose();

            _minimapControl?.DetachEditor();

            if (_breakpointMargin != null)
            {
                _breakpointMargin.BreakpointToggled -= OnBreakpointToggled;
                _textEditor.TextArea.LeftMargins.Remove(_breakpointMargin);
            }

            if (_metricsMargin != null)
            {
                _textEditor.TextArea.LeftMargins.Remove(_metricsMargin);
            }
        }
    }

    private void OnEditorAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_textEditor?.Parent is Grid grid && _minimapControl is null)
        {
            foreach (var child in grid.Children)
            {
                if (child is MinimapControl minimap)
                {
                    _minimapControl = minimap;
                    minimap.AttachEditor(_textEditor);
                    break;
                }
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
        else if (change.Property == SignatureHelpResultProperty)
        {
            var sigHelp = change.GetNewValue<SignatureHelp?>();
            if (sigHelp is { Signatures.Count: > 0 })
                ShowSignatureHelp(sigHelp);
        }
        else if (change.Property == FileMethodMetricsProperty)
        {
            var metrics = change.GetNewValue<IReadOnlyList<MethodMetrics>?>() ?? [];
            _metricsMargin?.UpdateMetrics(metrics);
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
        UpdateBracketHighlight();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space → request code completion
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            RequestCompletionCommand?.Execute(CreateLspContext());
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
        // Ctrl+D → add next occurrence of selection to multi-cursor
        else if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            HandleCtrlD();
        }
        // Ctrl+Alt+Up → add cursor above
        else if (e.Key == Key.Up && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            e.Handled = true;
            HandleAddCursorVertical(-1);
        }
        // Ctrl+Alt+Down → add cursor below
        else if (e.Key == Key.Down && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt))
        {
            e.Handled = true;
            HandleAddCursorVertical(1);
        }
        // Escape → clear multi-cursors
        else if (e.Key == Key.Escape && _multiCursorState is { IsActive: true })
        {
            e.Handled = true;
            _multiCursorState.Clear();
            _textEditor?.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        }
    }

    /// <summary>
    /// Creates an LspRequestContext with fresh text and caret position
    /// read directly from the TextEditor (not from bindings which may be stale).
    /// </summary>
    private LspRequestContext? CreateLspContext(string? triggerCharacter = null)
    {
        if (_textEditor is null) return null;
        var caret = _textEditor.TextArea.Caret;
        return new LspRequestContext(
            caret.Line,
            caret.Column,
            _textEditor.Document.Text,
            triggerCharacter);
    }

    // --- Multi-cursor helpers ---

    private void HandleCtrlD()
    {
        if (_textEditor is null || _multiCursorState is null) return;

        var selection = _textEditor.SelectedText;
        if (string.IsNullOrEmpty(selection))
        {
            // Select the word at caret
            var offset = _textEditor.CaretOffset;
            var doc = _textEditor.Document;
            var text = doc.Text;

            var start = offset;
            while (start > 0 && IsWordChar(text[start - 1])) start--;
            var end = offset;
            while (end < text.Length && IsWordChar(text[end])) end++;

            if (start < end)
            {
                _textEditor.Select(start, end - start);
            }
            return;
        }

        // Find next occurrence and add cursor there
        var afterOffset = _textEditor.SelectionStart + _textEditor.SelectionLength;
        var nextOffset = MultiCursorState.FindNextOccurrence(
            _textEditor.Document.Text, selection, afterOffset);

        if (nextOffset >= 0 && nextOffset != _textEditor.SelectionStart)
        {
            _multiCursorState.AddCursor(nextOffset);
            _textEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        }
    }

    private void HandleAddCursorVertical(int direction)
    {
        if (_textEditor is null || _multiCursorState is null) return;

        var caret = _textEditor.TextArea.Caret;
        var doc = _textEditor.Document;
        var targetLineNum = caret.Line + direction;

        if (targetLineNum < 1 || targetLineNum > doc.LineCount)
            return;

        var targetLine = doc.GetLineByNumber(targetLineNum);
        var columnInLine = caret.Column - 1; // 0-based within line
        var newOffset = _multiCursorState.AddCursorFromLine(
            _textEditor.CaretOffset,
            targetLine.Offset,
            targetLine.Length,
            columnInLine);

        if (newOffset.HasValue)
            _textEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    private void ApplyMultiCursorInsert(string insertedText)
    {
        if (_textEditor is null || _multiCursorState is null) return;

        _applyingMultiCursorEdits = true;
        try
        {
            // The primary cursor already inserted the text.
            // We need to know where it was BEFORE the insert to adjust secondary cursors.
            var primaryOldOffset = _textEditor.CaretOffset - insertedText.Length;
            var edits = _multiCursorState.GetInsertEdits(primaryOldOffset, insertedText);

            var doc = _textEditor.Document;
            doc.BeginUpdate();
            try
            {
                // Apply in reverse order (high-to-low offset) to preserve positions
                foreach (var (offset, text) in edits)
                {
                    if (offset >= 0 && offset <= doc.TextLength)
                        doc.Insert(offset, text);
                }
            }
            finally
            {
                doc.EndUpdate();
            }

            _multiCursorState.AdjustAfterSecondaryInserts(insertedText);
            _textEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        }
        finally
        {
            _applyingMultiCursorEdits = false;
        }
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Auto-triggers completion after typing trigger characters (., &lt;)
    /// or signature help after ( and ,
    /// or after a short delay when typing identifier characters.
    /// </summary>
    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text))
            return;

        // Apply multi-cursor inserts for the typed text
        if (!_applyingMultiCursorEdits && _multiCursorState is { IsActive: true } && _textEditor is not null)
        {
            ApplyMultiCursorInsert(text);
        }

        var ch = text[0];

        // Signature help trigger characters
        if (ch is '(' or ',')
        {
            RequestSignatureHelpCommand?.Execute(CreateLspContext(ch.ToString()));
            return;
        }

        // Close signature help on )
        if (ch is ')')
        {
            _insightWindow?.Close();
            return;
        }

        if (_completionWindow is not null || RequestCompletionCommand is null)
            return;

        // Completion trigger characters
        if (ch is '.' or '<' or ':')
        {
            RequestCompletionCommand.Execute(CreateLspContext(ch.ToString()));
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
                            RequestCompletionCommand?.Execute(CreateLspContext());
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

        // Move StartOffset back to the start of the partial word so the
        // completion replaces the already-typed prefix instead of appending.
        var caretOffset = _textEditor.TextArea.Caret.Offset;
        var document = _textEditor.Document;
        var wordStart = caretOffset;
        while (wordStart > 0 && (char.IsLetterOrDigit(document.GetCharAt(wordStart - 1)) || document.GetCharAt(wordStart - 1) == '_'))
            wordStart--;
        _completionWindow.StartOffset = wordStart;

        foreach (var item in items)
        {
            _completionWindow.CompletionList.CompletionData.Add(new LspCompletionData(item));
        }

        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    public void ShowSignatureHelp(SignatureHelp sigHelp)
    {
        if (_textEditor is null || sigHelp.Signatures.Count == 0)
            return;

        _insightWindow?.Close();

        _insightWindow = new OverloadInsightWindow(_textEditor.TextArea);
        _insightWindow.Provider = new SignatureOverloadProvider(sigHelp);
        _insightWindow.Show();
        _insightWindow.Closed += (_, _) => _insightWindow = null;
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

    private void UpdateBracketHighlight()
    {
        if (_bracketRenderer is null || _textEditor?.Document is null)
            return;

        var offset = _textEditor.TextArea.Caret.Offset;
        var text = _textEditor.Document.Text;
        var pair = BracketMatcher.FindMatchingBracket(text, offset);

        _bracketRenderer.UpdateBracketPair(pair);
        _textEditor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Caret);
    }

    private async void OnTextViewPointerHover(object? sender, PointerEventArgs e)
    {
        var evaluateFunc = DebugEvaluateFunc;
        if (evaluateFunc is null || _textEditor?.Document is null)
            return;

        var pos = _textEditor.TextArea.TextView.GetPositionFloor(e.GetPosition(_textEditor.TextArea.TextView));
        if (pos is null)
            return;

        var offset = _textEditor.Document.GetOffset(pos.Value.Location);
        var word = GetWordAtOffset(_textEditor.Document, offset);
        if (string.IsNullOrWhiteSpace(word))
            return;

        // Skip if already showing tooltip for this word
        if (_debugHoverShowing && word == _lastHoverWord)
            return;

        _lastHoverWord = word;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        var cts = _hoverCts = new CancellationTokenSource();

        try
        {
            var result = await evaluateFunc(word, cts.Token);
            if (cts.Token.IsCancellationRequested || string.IsNullOrEmpty(result))
                return;

            ShowDebugHoverPopup(word, result);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Hover] Evaluate failed for '{Word}'", word);
        }
    }

    private void OnTextViewPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        _hoverCts?.Cancel();
        _lastHoverWord = null;
        CloseDebugHoverPopup();
    }

    private void ShowDebugHoverPopup(string expression, string value)
    {
        CloseDebugHoverPopup();

        if (_textEditor?.TextArea?.TextView is not { } textView)
            return;

        var content = new Avalonia.Controls.TextBlock
        {
            Text = $"{expression} = {value}",
            FontFamily = new Avalonia.Media.FontFamily("Cascadia Code, Consolas, Courier New, monospace"),
            FontSize = 13,
        };

        Avalonia.Controls.ToolTip.SetTip(textView, content);
        Avalonia.Controls.ToolTip.SetPlacement(textView, Avalonia.Controls.PlacementMode.Pointer);
        Avalonia.Controls.ToolTip.SetShowDelay(textView, 0);
        Avalonia.Controls.ToolTip.SetIsOpen(textView, true);
        _debugHoverShowing = true;
    }

    private void CloseDebugHoverPopup()
    {
        if (_debugHoverShowing && _textEditor?.TextArea?.TextView is { } textView)
        {
            Avalonia.Controls.ToolTip.SetIsOpen(textView, false);
            Avalonia.Controls.ToolTip.SetTip(textView, null);
            _debugHoverShowing = false;
        }
    }

    private static string? GetWordAtOffset(AvaloniaEdit.Document.TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength)
            return null;

        int start = offset;
        while (start > 0 && IsIdentifierChar(document.GetCharAt(start - 1)))
            start--;

        int end = offset;
        while (end < document.TextLength && IsIdentifierChar(document.GetCharAt(end)))
            end++;

        if (start == end)
            return null;

        return document.GetText(start, end - start);
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}
