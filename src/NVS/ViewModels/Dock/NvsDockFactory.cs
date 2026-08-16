using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;

namespace NVS.ViewModels.Dock;

public sealed class NvsDockFactory : Factory
{
    private readonly MainViewModel _main;
    private readonly NVS.Core.Models.Settings.DockLayoutSettings _dockSettings;
    private IRootDock? _rootDock;
    private IDocumentDock? _documentDock;
    private ProportionalDock? _mainLayout;
    private ProportionalDock? _rightDock;
    private ProportionalDockSplitter? _rightSplitter;
    private ToolDock? _leftToolDock;
    private ToolDock? _bottomToolDock;
    private SymbolsToolViewModel? _symbolsTool;
    private TestExplorerToolViewModel? _testExplorerTool;
    private ExplorerToolViewModel? _explorerTool;
    private SearchToolViewModel? _searchTool;
    private GitToolViewModel? _gitTool;
    private TerminalToolViewModel? _terminalTool;
    private NuGetToolViewModel? _nugetTool;
    private ContainersToolViewModel? _containersTool;
    private HelpToolViewModel? _helpTool;
    private CodeMetricsToolViewModel? _codeMetricsTool;

    public DiffViewerToolViewModel? DiffViewer { get; private set; }
    public ConflictResolverToolViewModel? ConflictResolver { get; private set; }
    public LlmChatToolViewModel? LlmChat { get; private set; }
    public DatabaseExplorerToolViewModel? DatabaseExplorer { get; private set; }
    public ApiClientToolViewModel? ApiClient { get; private set; }
    public TerminalToolViewModel? TerminalTool => _terminalTool;
    public NuGetToolViewModel? NuGetTool => _nugetTool;
    public ContainersToolViewModel? ContainersTool => _containersTool;
    public HelpToolViewModel? HelpTool => _helpTool;
    public CodeMetricsToolViewModel? CodeMetricsTool => _codeMetricsTool;
    public SymbolsToolViewModel? SymbolsTool => _symbolsTool;
    public TestExplorerToolViewModel? TestExplorerTool => _testExplorerTool;
    public ExplorerToolViewModel? ExplorerTool => _explorerTool;
    public SearchToolViewModel? SearchTool => _searchTool;
    public GitToolViewModel? GitTool => _gitTool;
    public NvsDockFactory(MainViewModel main, NVS.Core.Models.Settings.DockLayoutSettings? dockSettings = null)
    {
        _main = main;
        _dockSettings = dockSettings ?? new NVS.Core.Models.Settings.DockLayoutSettings();
    }

    public override IRootDock CreateLayout()
    {
        var explorer = new ExplorerToolViewModel(_main);
        var search = new SearchToolViewModel(_main);
        var git = new GitToolViewModel(_main);
        var symbols = new SymbolsToolViewModel(_main, _main.Editor?.LspSessionManager);
        var testExplorer = new TestExplorerToolViewModel(_main, _main.TestExplorerService);
        var terminal = new TerminalToolViewModel(_main);
        var buildOutput = new BuildOutputToolViewModel(_main);
        var problems = new ProblemsToolViewModel(_main);
        var callStack = new CallStackToolViewModel(_main);
        var variables = new VariablesToolViewModel(_main);
        var dbExplorer = new DatabaseExplorerToolViewModel(_main);
        var apiClient = new ApiClientToolViewModel(_main);
        DatabaseExplorer = dbExplorer;
        ApiClient = apiClient;
        var llmChat = new LlmChatToolViewModel(_main);
        var nuget = new NuGetToolViewModel(_main);
        var containers = new ContainersToolViewModel(_main);
        var help = new HelpToolViewModel();
        var codeMetrics = new CodeMetricsToolViewModel(_main);
        var conflictResolver = new ConflictResolverToolViewModel(_main);
        var welcome = new WelcomeDocumentViewModel(_main);
        var editor = new EditorDocumentViewModel(_main);

        var leftDock = new ProportionalDock
        {
            Proportion = _dockSettings.LeftPanelProportion,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = explorer,
                    VisibleDockables = CreateList<IDockable>(explorer, search, git, symbols, testExplorer),
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible,
                }
            ),
        };
        _leftToolDock = (ToolDock)leftDock.VisibleDockables![0];
        _symbolsTool = symbols;
        _testExplorerTool = testExplorer;
        _explorerTool = explorer;
        _searchTool = search;
        _gitTool = git;

        var bottomToolDock = new ToolDock
        {
            ActiveDockable = terminal,
            VisibleDockables = CreateList<IDockable>(terminal, buildOutput, problems, callStack, variables, nuget, containers, codeMetrics, help, conflictResolver),
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible,
        };
        _bottomToolDock = bottomToolDock;

        var bottomDock = new ProportionalDock
        {
            Proportion = _dockSettings.BottomPanelProportion,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                bottomToolDock
            ),
        };

        _terminalTool = terminal;
        _nugetTool = nuget;
        _containersTool = containers;
        _helpTool = help;
        _codeMetricsTool = codeMetrics;

        var documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            ActiveDockable = welcome,
            VisibleDockables = CreateList<IDockable>(welcome, editor),
            CanCreateDocument = false,
        };

        var centerWithBottom = new ProportionalDock
        {
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                documentDock,
                new ProportionalDockSplitter(),
                bottomDock
            )
        };

        var rightDock = new ProportionalDock
        {
            Proportion = _dockSettings.RightPanelProportion,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = llmChat,
                    VisibleDockables = CreateList<IDockable>(llmChat),
                    Alignment = Alignment.Right,
                    GripMode = GripMode.Visible,
                }
            ),
        };

        var rightSplitter = new ProportionalDockSplitter();

        var mainLayout = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                leftDock,
                new ProportionalDockSplitter(),
                centerWithBottom,
                rightSplitter,
                rightDock
            )
        };

        _mainLayout = mainLayout;
        _rightDock = rightDock;
        _rightSplitter = rightSplitter;

        // Honor the "Enable AI chat panel" setting at startup
        if (!_main.SettingsService.AppSettings.Llm.EnableChat)
        {
            mainLayout.VisibleDockables.Remove(rightSplitter);
            mainLayout.VisibleDockables.Remove(rightDock);
        }

        var homeView = new HomeViewModel
        {
            Id = "Home",
            Title = "Home",
            ActiveDockable = mainLayout,
            VisibleDockables = CreateList<IDockable>(mainLayout),
        };

        var rootDock = CreateRootDock();
        rootDock.IsCollapsable = false;
        rootDock.ActiveDockable = homeView;
        rootDock.DefaultDockable = homeView;
        rootDock.VisibleDockables = CreateList<IDockable>(homeView);
        rootDock.LeftPinnedDockables = CreateList<IDockable>();
        rootDock.RightPinnedDockables = CreateList<IDockable>();
        rootDock.TopPinnedDockables = CreateList<IDockable>();
        rootDock.BottomPinnedDockables = CreateList<IDockable>();

        _documentDock = documentDock;
        _rootDock = rootDock;
        ConflictResolver = conflictResolver;
        LlmChat = llmChat;

        return rootDock;
    }

    public DiffViewerToolViewModel OpenDiffDocument()
    {
        // Reuse existing diff tab if open
        if (DiffViewer is not null && _documentDock?.VisibleDockables?.Contains(DiffViewer) == true)
        {
            _documentDock.ActiveDockable = DiffViewer;
            return DiffViewer;
        }

        var diffViewer = new DiffViewerToolViewModel(_main);
        DiffViewer = diffViewer;

        if (ContextLocator is Dictionary<string, Func<object?>> ctx)
            ctx["DiffViewer"] = () => _main;

        _documentDock?.VisibleDockables?.Add(diffViewer);
        if (_documentDock is not null)
            _documentDock.ActiveDockable = diffViewer;

        return diffViewer;
    }

    /// <summary>
    /// Opens the Database Explorer as a document tab (creating the tab on first use,
    /// reactivating it afterwards — including after it was closed).
    /// </summary>
    public DatabaseExplorerToolViewModel OpenDatabaseExplorerDocument()
    {
        System.Diagnostics.Debug.Assert(DatabaseExplorer is not null, "DatabaseExplorer is created in CreateLayout");
        return OpenDocument(DatabaseExplorer!);
    }

    /// <summary>
    /// Creates and opens a NEW Database Explorer document with its own connection —
    /// allowing many databases to be open concurrently, each in its own tab.
    /// </summary>
    public DatabaseExplorerToolViewModel CreateDatabaseExplorerDocument(string title)
    {
        var dbExplorer = new DatabaseExplorerToolViewModel(_main) { Title = title };
        dbExplorer.Id = $"DatabaseExplorer-{System.Guid.NewGuid():N}";
        return OpenDocument(dbExplorer);
    }

    /// <summary>
    /// Opens the API Client as a document tab (creating the tab on first use,
    /// reactivating it afterwards — including after it was closed).
    /// </summary>
    public ApiClientToolViewModel OpenApiClientDocument()
    {
        System.Diagnostics.Debug.Assert(ApiClient is not null, "ApiClient is created in CreateLayout");
        return OpenDocument(ApiClient!);
    }

    private T OpenDocument<T>(T document) where T : global::Dock.Model.Core.IDockable
    {
        if (_documentDock?.VisibleDockables?.Contains(document) != true)
        {
            _documentDock?.VisibleDockables?.Add(document);
        }

        if (_documentDock is not null)
        {
            _documentDock.ActiveDockable = document;
        }

        return document;
    }

    /// <summary>Shows or hides the right-side LLM chat panel (Settings → LLM → Enable chat panel).</summary>
    public void SetChatPanelVisible(bool visible)
    {
        if (_mainLayout?.VisibleDockables is null || _rightDock is null || _rightSplitter is null)
        {
            return;
        }

        var isVisible = _mainLayout.VisibleDockables.Contains(_rightDock);
        if (visible == isVisible)
        {
            return;
        }

        if (visible)
        {
            _mainLayout.VisibleDockables.Add(_rightSplitter);
            _mainLayout.VisibleDockables.Add(_rightDock);
        }
        else
        {
            _mainLayout.VisibleDockables.Remove(_rightSplitter);
            _mainLayout.VisibleDockables.Remove(_rightDock);
        }
    }

    /// <summary>Shows the LLM chat panel (un-hiding it first when disabled) and activates it.</summary>
    public void ShowLlmChat()
    {
        SetChatPanelVisible(true);
        if (_rightDock?.VisibleDockables?.Count > 0 && LlmChat is not null)
        {
            var toolDock = _rightDock.VisibleDockables
                .OfType<ToolDock>()
                .FirstOrDefault(d => d.VisibleDockables?.Contains(LlmChat) == true);
            if (toolDock is not null)
            {
                toolDock.ActiveDockable = LlmChat;
            }
        }
    }

    /// <summary>
    /// Shows a bottom-dock tool, re-adding it first if the user previously closed it.
    /// </summary>
    public void ShowToolInBottomDock(IDockable? tool)
    {
        if (tool is null || _bottomToolDock?.VisibleDockables is null)
        {
            return;
        }

        if (!_bottomToolDock.VisibleDockables.Contains(tool))
        {
            _bottomToolDock.VisibleDockables.Add(tool);
        }

        _bottomToolDock.ActiveDockable = tool;
    }

    /// <summary>Shows a left-dock tool, re-adding it first if the user previously closed it.</summary>
    public void ShowToolInLeftDock(IDockable? tool)
    {
        if (tool is null || _leftToolDock?.VisibleDockables is null)
        {
            return;
        }

        if (!_leftToolDock.VisibleDockables.Contains(tool))
        {
            _leftToolDock.VisibleDockables.Add(tool);
        }

        _leftToolDock.ActiveDockable = tool;
    }

    public override IDockWindow? CreateWindowFrom(IDockable dockable)
    {
        var window = base.CreateWindowFrom(dockable);
        if (window is not null)
        {
            window.Title = "NVS";
        }
        return window;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["Explorer"] = () => _main,
            ["Search"] = () => _main,
            ["Git"] = () => _main,
            ["Symbols"] = () => _main,
            ["Terminal"] = () => _main,
            ["BuildOutput"] = () => _main,
            ["Problems"] = () => _main,
            ["CallStack"] = () => _main,
            ["Variables"] = () => _main,
            ["DatabaseExplorer"] = () => _main,
            ["ApiClient"] = () => _main,
            ["LlmChat"] = () => _main,
            ["NuGet"] = () => _main,
            ["Containers"] = () => _main,
            ["Help"] = () => _main,
            ["CodeMetrics"] = () => _main,
            ["DiffViewer"] = () => _main,
            ["ConflictResolver"] = () => _main,
            ["Welcome"] = () => _main,
            ["Editor"] = () => _main,
            ["Home"] = () => _main,
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock,
            ["Documents"] = () => _documentDock,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => DockSettings.UseManagedWindows
                ? new ManagedHostWindow()
                : new HostWindow(),
        };

        base.InitLayout(layout);
    }
}

public class HomeViewModel : RootDock
{
}
