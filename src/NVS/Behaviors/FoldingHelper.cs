using System.Timers;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using NVS.Core.Enums;

namespace NVS.Behaviors;

/// <summary>
/// Manages code folding for a TextEditor, selecting the appropriate folding strategy
/// based on the document language and updating foldings on text changes (debounced).
/// </summary>
public sealed class FoldingHelper : IDisposable
{
    private readonly TextEditor _editor;
    private readonly FoldingManager _foldingManager;
    private readonly System.Timers.Timer _debounceTimer;
    private Language _language;

    private static readonly HashSet<Language> BraceLanguages =
    [
        Language.CSharp, Language.Cpp, Language.C, Language.Java, Language.JavaScript,
        Language.TypeScript, Language.Rust, Language.Go, Language.Php, Language.Json,
        Language.Css,
    ];

    private static readonly HashSet<Language> IndentLanguages =
    [
        Language.Python, Language.Yaml,
    ];

    public FoldingHelper(TextEditor editor, Language language)
    {
        _editor = editor;
        _language = language;
        _foldingManager = FoldingManager.Install(editor.TextArea);

        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;

        _editor.TextChanged += OnTextChanged;
        UpdateFoldings();
    }

    public void SetLanguage(Language language)
    {
        _language = language;
        UpdateFoldings();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateFoldings);
    }

    private void UpdateFoldings()
    {
        var document = _editor.Document;
        if (document is null || document.TextLength == 0)
            return;

        IEnumerable<NewFolding>? foldings = null;

        if (BraceLanguages.Contains(_language))
        {
            var strategy = new BraceFoldingStrategy();
            foldings = strategy.CreateFoldings(document);
        }
        else if (IndentLanguages.Contains(_language))
        {
            var strategy = new IndentFoldingStrategy();
            foldings = strategy.CreateFoldings(document);
        }

        if (foldings is not null)
        {
            _foldingManager.UpdateFoldings(foldings, firstErrorOffset: -1);
        }
    }

    public void Dispose()
    {
        _editor.TextChanged -= OnTextChanged;
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
        FoldingManager.Uninstall(_foldingManager);
    }
}
