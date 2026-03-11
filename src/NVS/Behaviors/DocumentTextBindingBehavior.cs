using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using CommunityToolkit.Mvvm.Input;

namespace NVS.Behaviors;

public class DocumentTextBindingBehavior : Behavior<TextEditor>
{
    private TextEditor? _textEditor;
    private SearchPanel? _searchPanel;
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

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is TextEditor textEditor)
        {
            _textEditor = textEditor;
            _textEditor.TextChanged += OnEditorTextChanged;
            _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            UpdateCaretPosition();

            _searchPanel = SearchPanel.Install(_textEditor);

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

    private void UpdateCaretPosition()
    {
        if (_textEditor != null)
        {
            Line = _textEditor.TextArea.Caret.Line;
            Column = _textEditor.TextArea.Caret.Column;
        }
    }
}
