using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace NVS.ViewModels.Dock;

public sealed partial class HelpToolViewModel : Tool, INotifyPropertyChanged
{
    private string _searchQuery = string.Empty;
    private HelpTopic? _selectedTopic;

    public ObservableCollection<HelpTopic> AllTopics { get; } = [];
    public ObservableCollection<HelpTopic> FilteredTopics { get; } = [];

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterTopics();
            }
        }
    }

    public HelpTopic? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (_selectedTopic != value)
            {
                _selectedTopic = value;
                OnPropertyChanged();
            }
        }
    }

    public HelpToolViewModel()
    {
        Id = "Help";
        Title = "❓ Help";
        CanClose = true;
        CanPin = true;
        LoadBuiltInTopics();
        FilterTopics();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    private void FilterTopics()
    {
        FilteredTopics.Clear();
        var query = SearchQuery.Trim();

        foreach (var topic in AllTopics)
        {
            if (string.IsNullOrEmpty(query) || MatchesTopic(topic, query))
                FilteredTopics.Add(topic);
        }

        if (SelectedTopic is null || !FilteredTopics.Contains(SelectedTopic))
            SelectedTopic = FilteredTopics.Count > 0 ? FilteredTopics[0] : null;
    }

    private static bool MatchesTopic(HelpTopic topic, string query)
    {
        return topic.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || topic.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || topic.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadBuiltInTopics()
    {
        AllTopics.Add(new HelpTopic("Getting Started", "Basics", GetGettingStartedContent()));
        AllTopics.Add(new HelpTopic("Keyboard Shortcuts", "Reference", GetKeyboardShortcutsContent()));
        AllTopics.Add(new HelpTopic("Editor Features", "Editor", GetEditorFeaturesContent()));
        AllTopics.Add(new HelpTopic("Build & Run", "Build", GetBuildRunContent()));
        AllTopics.Add(new HelpTopic("Debugging", "Debug", GetDebuggingContent()));
        AllTopics.Add(new HelpTopic("Terminal", "Tools", GetTerminalContent()));
        AllTopics.Add(new HelpTopic("Git Integration", "Source Control", GetGitContent()));
        AllTopics.Add(new HelpTopic("Settings", "Configuration", GetSettingsContent()));
        AllTopics.Add(new HelpTopic("Language Server Setup", "Configuration", GetLspContent()));
        AllTopics.Add(new HelpTopic("NuGet Packages", "Tools", GetNuGetContent()));
        AllTopics.Add(new HelpTopic("LLM Chat", "Tools", GetLlmContent()));
        AllTopics.Add(new HelpTopic("SQLite Explorer", "Tools", GetSqliteContent()));
    }

    private new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }

    #region Help Content

    internal static string GetGettingStartedContent() =>
        """
        GETTING STARTED WITH NVS

        NVS (No Vim Substitute) is a cross-platform IDE built with .NET and Avalonia.

        Quick Start:
        • File → Open Folder to open a workspace
        • File → Open Solution to load a .sln or .slnx file
        • File → New File (Ctrl+N) to create a new file
        • Use the Solution Explorer (left panel) to browse your project

        Layout:
        • Left Panel: Explorer, Search, Source Control
        • Center: Editor with tabs for open files
        • Bottom Panel: Terminal, Build Output, Problems, Code Metrics
        • Right Panel: LLM Chat assistant
        • Status Bar: Line/column, branch, diagnostics, method metrics

        All panels are dockable — drag tabs to rearrange the layout.

        AI Features:
        Configure an LLM in Settings → LLM to enable the chat assistant
        and inline ghost-text code completions (Tab to accept).
        """;

    internal static string GetKeyboardShortcutsContent() =>
        """
        KEYBOARD SHORTCUTS

        FILE
          Ctrl+N            New file
          Ctrl+O            Open file
          Ctrl+S            Save
          Ctrl+Shift+S      Save As
          Ctrl+W            Close tab

        EDITING
          Ctrl+Z            Undo
          Ctrl+Y            Redo
          Ctrl+X            Cut
          Ctrl+C            Copy
          Ctrl+V            Paste
          Ctrl+F            Find
          Ctrl+H            Replace
          Ctrl+G            Go to line
          Ctrl+D            Select next occurrence (multi-cursor)
          Ctrl+Alt+Up       Add cursor above
          Ctrl+Alt+Down     Add cursor below
          Escape            Clear extra cursors / dismiss ghost text

        CODE
          Ctrl+Space        Autocomplete
          F12               Go to definition
          Shift+F12         Find references
          Ctrl+.            Code actions / quick fixes
          F2                Rename symbol
          Tab               Accept ghost-text suggestion
          Escape            Dismiss ghost-text suggestion

        BUILD & RUN
          Ctrl+Shift+B      Build solution
          Ctrl+F5           Run without debugging
          Ctrl+Shift+C      Clean solution

        DEBUG
          F5                Start / Continue debugging
          Shift+F5          Stop debugging
          F9                Toggle breakpoint
          F10               Step over
          F11               Step into
          Shift+F11         Step out

        EDITOR LAYOUT
          Ctrl+\            Split editor vertically
          Ctrl+Shift+\      Split editor horizontally

        NAVIGATION
          Ctrl+Shift+E      Explorer panel
          Ctrl+Shift+F      Search panel
          Ctrl+Shift+G      Source Control panel
          Ctrl+Shift+O      Command palette
          Ctrl+`            Terminal panel

        HELP
          F1                Help panel
        """;

    internal static string GetEditorFeaturesContent() =>
        """
        EDITOR FEATURES

        Syntax Highlighting:
        NVS provides regex-based syntax highlighting for 16 languages including
        C#, Python, JavaScript, TypeScript, Rust, Go, Java, PHP, C/C++, HTML,
        CSS, JSON, XML, YAML, Markdown, and TOML.

        Autocomplete:
        Press Ctrl+Space to trigger autocomplete. When a Language Server is
        configured, completions come from the LSP. Roslyn provides rich C#
        completions, hover tooltips, go-to-definition, references, signature
        help, code actions, and formatting — all in-process.

        Inline Ghost-Text Completions:
        When an LLM is configured and auto-complete is enabled in Settings,
        NVS suggests code as dimmed ghost text after the cursor. Press Tab
        to accept the suggestion, or Escape to dismiss it. Suggestions are
        triggered after a short pause while typing.

        Go to Definition:
        Press F12 to navigate to the definition of a symbol (requires LSP).
        Shift+F12 to find all references. Ctrl+. for code actions.

        Bracket Matching:
        Matching brackets () {} [] are highlighted when the caret is adjacent.

        Code Folding:
        Brace-based folding for C-style languages, indentation-based for
        Python and YAML. Click the fold markers in the gutter to collapse.

        Minimap:
        A scaled document overview appears to the right of the editor.
        Click anywhere on the minimap to scroll to that position.

        Split Editor:
        Ctrl+\ splits the editor vertically, Ctrl+Shift+\ horizontally.
        Also available via right-click context menu and View menu.

        Multiple Cursors:
        Ctrl+D selects the next occurrence of the current selection.
        Ctrl+Alt+Up/Down adds a cursor above or below. Escape clears
        extra cursors. Type once to edit at all cursor positions.

        Find & Replace:
        Ctrl+F opens the find bar, Ctrl+H opens find and replace.

        Line Numbers & Current Line:
        Line and column numbers are shown in the status bar. The current line
        is highlighted. Code metrics dots appear in the gutter for C# methods.

        Undo/Redo:
        Full undo/redo stack with Ctrl+Z and Ctrl+Y.
        """;

    internal static string GetBuildRunContent() =>
        """
        BUILD & RUN

        Building:
        • Ctrl+Shift+B builds the current solution
        • Build → Rebuild Solution for a full rebuild
        • Build → Clean Solution removes build artifacts
        • Build output appears in the Build Output panel (bottom)
        • Errors and warnings appear in the Problems panel

        Running:
        • Ctrl+F5 runs the startup project without debugging
        • The startup project is auto-detected from the solution
        • Output appears in the Build Output panel

        Requirements:
        • .NET SDK must be installed and on PATH
        • A .sln, .slnx, or .csproj file must be loaded
        """;

    internal static string GetDebuggingContent() =>
        """
        DEBUGGING

        NVS uses the Debug Adapter Protocol (DAP) with netcoredbg for .NET debugging.

        Starting a Debug Session:
        • Press F5 to start debugging
        • The debugger will build and launch the startup project
        • netcoredbg must be installed and on PATH

        Breakpoints:
        • Press F9 to toggle a breakpoint on the current line
        • Breakpoints are shown as red circles in the editor gutter
        • Breakpoints persist across sessions

        Stepping:
        • F10 — Step over (execute current line)
        • F11 — Step into (enter function calls)
        • Shift+F11 — Step out (return from current function)
        • F5 — Continue (resume execution)

        Debug Panels:
        • Call Stack — shows the execution stack frames
        • Variables — shows local variables and their values
        • Both panels update when paused at a breakpoint

        Installing netcoredbg:
        Download from https://github.com/Samsung/netcoredbg/releases
        and add to your PATH.
        """;

    internal static string GetTerminalContent() =>
        """
        TERMINAL

        NVS includes an integrated terminal using your system shell.

        Opening:
        • Terminal → New Terminal from the menu
        • Ctrl+` toggles the terminal panel
        • The terminal opens in the bottom panel

        Features:
        • Full PTY support (colors, cursor movement)
        • Working directory matches the open workspace
        • Multiple terminal instances supported
        • Copy/paste with standard shortcuts
        """;

    internal static string GetGitContent() =>
        """
        GIT INTEGRATION

        NVS provides built-in Git support via the Source Control panel.

        Opening:
        • Ctrl+Shift+G or click the Source Control icon in the left panel

        Features:
        • View changed, staged, and untracked files
        • Stage/unstage individual files or partial hunks
        • Commit with a message, amend last commit
        • Side-by-side diff viewer — click any file to see changes
        • Merge conflict resolution — 3-pane resolver with Accept Current,
          Accept Incoming, and Accept Both per conflict block
        • Branch management — create, checkout, delete, list
        • Commit log and branch picker in status bar
        • Reset (soft/mixed/hard), rebase onto branch
        • Stash management and tag support

        Requirements:
        • Git must be installed on your system
        • The workspace must be a Git repository
        """;

    internal static string GetSettingsContent() =>
        """
        SETTINGS

        Open Settings from the menu (gear icon) or Help → Settings.

        General:
          Theme selection (NVS Dark, NVS Light, Monokai, Solarized Dark),
          keybinding preset, locale, auto-update check,
          session restore preference.

        Editor:
          Font family, font size, tab size, word wrap, line numbers,
          minimap, auto-indent, bracket matching, whitespace rendering.

        Terminal:
          Shell path and arguments.

        Language Servers:
          Configure LSP servers per language. NVS includes a built-in
          registry of 14 common language servers with one-click install.

        LLM:
          Configure AI assistant endpoint, model, API key,
          temperature, streaming, prompt templates, auto-complete
          (ghost-text), and vision/image support for multimodal models.

        Settings are stored in your user profile and persist across sessions.
        """;

    internal static string GetLspContent() =>
        """
        LANGUAGE SERVER SETUP

        NVS supports the Language Server Protocol (LSP) for rich editor features.

        Supported Servers (auto-detected on PATH):
          • C#: csharp-ls
          • Python: Pyright, pylsp
          • JavaScript/TypeScript: typescript-language-server
          • Rust: rust-analyzer
          • Go: gopls
          • Java: jdtls
          • C/C++: clangd
          • HTML/CSS: vscode-html-language-server
          • JSON: vscode-json-language-server
          • YAML: yaml-language-server
          • Lua: lua-language-server
          • Bash: bash-language-server

        Installation:
        Most servers can be installed via npm, pip, or your package manager.
        Configure custom paths in Settings → Language Servers.

        Features provided by LSP:
        • Autocomplete (Ctrl+Space)
        • Go to definition (F12)
        • Diagnostics (error squiggles)
        • Hover information
        """;

    internal static string GetNuGetContent() =>
        """
        NUGET PACKAGES

        The NuGet panel (bottom panel) provides a visual package manager.

        Tabs:
          Browse — Search nuget.org for packages
          Installed — View packages in the selected project
          Updates — Check for available updates

        Operations:
          • Select a project from the dropdown
          • Search and install packages
          • Uninstall packages
          • Update to latest versions
          • Restore all packages

        The panel wraps the dotnet CLI for all operations.
        """;

    internal static string GetLlmContent() =>
        """
        LLM CHAT

        NVS includes a built-in AI chat assistant (right panel).

        Setup:
        Configure the LLM endpoint in Settings → LLM:
          • Endpoint URL (any OpenAI-compatible API)
          • API Key
          • Model name

        Supported providers:
        OpenAI, OpenRouter, DeepSeek, Ollama, LM Studio,
        llama.cpp, and any OpenAI-compatible endpoint.

        Task Modes:
          General — Questions and explanations
          Coding — Code generation and refactoring
          Debugging — Bug diagnosis and fixes
          Testing — Test creation and coverage

        Agent Tools:
        The assistant has 12 built-in tools:
          • File read/write, search, and editor integration
          • Build and test execution
          • Terminal command execution
          • Git status and diff
          • Workspace diagnostics

        Chat Sessions:
        Conversations are saved per workspace and persist across
        restarts. Use the session dropdown to create, switch between,
        or delete sessions. Each session has an auto-generated title.

        Code Blocks:
        Assistant responses render code with syntax highlighting.
        Use the Copy button to copy code, or Apply to insert it
        directly into the active editor.

        Context Enrichment:
        The chat automatically includes context about open files,
        diagnostics, git status, and the current branch. Use the
        📎 button to attach additional files to your message.

        Vision Support:
        When enabled in Settings → LLM → Supports Vision, use the
        📷 button to attach images (e.g. screenshots of UI issues).
        Images are sent as base64 data URIs to the model.

        Inline Ghost-Text Completions:
        Enable auto-complete in Settings → LLM to get LLM-powered
        code suggestions as you type. Ghost text appears dimmed
        after the cursor — press Tab to accept, Escape to dismiss.
        """;

    internal static string GetSqliteContent() =>
        """
        SQLITE EXPLORER

        The Database Explorer panel lets you browse and query SQLite databases.

        Opening a Database:
        • Double-click a .db, .sqlite, or .sqlite3 file in the explorer
        • Or use the Database Explorer panel in the bottom area

        Features:
        • Browse tables and their schemas
        • Execute SQL queries
        • View query results in a grid
        • Supports multiple databases simultaneously
        """;

    #endregion
}

public sealed record HelpTopic(string Title, string Category, string Content);
