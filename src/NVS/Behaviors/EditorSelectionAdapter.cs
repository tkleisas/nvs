using AvaloniaEdit;

namespace NVS.Behaviors;

/// <summary>
/// Selection/caret operations of a code editor, abstracted so view models
/// (and tests) don't depend on a live TextEditor.
/// </summary>
public interface IEditorSelection
{
    /// <summary>Whether a non-empty selection exists.</summary>
    bool HasSelection { get; }

    /// <summary>The selected text, or empty when there is no selection.</summary>
    string SelectedText { get; }

    /// <summary>The entire document text.</summary>
    string AllText { get; }

    /// <summary>Replaces the current selection with text, or inserts at the caret when nothing is selected.</summary>
    void ReplaceSelectionOrInsertAtCaret(string text);
}

/// <summary>
/// Thin adapter over a <see cref="TextEditor"/> exposing selection/caret operations
/// to view models (used by inline AI edit) without leaking the editor into them.
/// </summary>
public sealed class EditorSelectionAdapter : IEditorSelection
{
    private readonly TextEditor _editor;

    public EditorSelectionAdapter(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>Whether a non-empty selection exists.</summary>
    public bool HasSelection => _editor.SelectionLength > 0;

    /// <summary>The selected text, or empty when there is no selection.</summary>
    public string SelectedText => _editor.SelectionLength > 0
        ? _editor.Document.GetText(_editor.SelectionStart, _editor.SelectionLength)
        : string.Empty;

    /// <summary>The entire document text.</summary>
    public string AllText => _editor.Document.Text;

    /// <summary>The language hint for prompts (from the document's highlighting language, if any).</summary>
    public string Language { get; set; } = "code";

    /// <summary>Replaces the current selection with text, or inserts at the caret when nothing is selected.</summary>
    public void ReplaceSelectionOrInsertAtCaret(string text)
    {
        var offset = _editor.SelectionLength > 0 ? _editor.SelectionStart : _editor.TextArea.Caret.Offset;
        _editor.Document.Replace(offset, _editor.SelectionLength, text);
        _editor.TextArea.Caret.Offset = offset + text.Length;
        _editor.TextArea.Focus();
    }
}
