using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using NVS.Core.Interfaces;

namespace NVS.Behaviors;

/// <summary>
/// Adapts an NVS CompletionItem to AvaloniaEdit's ICompletionData interface.
/// </summary>
public sealed class LspCompletionData : ICompletionData
{
    private readonly CompletionItem _item;

    public LspCompletionData(CompletionItem item)
    {
        _item = item;
    }

    public IImage? Image => null;

    public string Text => _item.InsertText ?? _item.Label;

    public object Content => _item.Label;

    public object Description => _item.Detail ?? _item.Documentation ?? string.Empty;

    public double Priority => _item.Kind switch
    {
        CompletionItemKind.Keyword => 1.0,
        CompletionItemKind.Snippet => 0.9,
        CompletionItemKind.Method or CompletionItemKind.Function => 0.8,
        CompletionItemKind.Property or CompletionItemKind.Field => 0.7,
        CompletionItemKind.Class or CompletionItemKind.Interface => 0.6,
        _ => 0.5,
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
