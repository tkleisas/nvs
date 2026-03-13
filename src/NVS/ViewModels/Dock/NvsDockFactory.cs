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
        var terminal = new TerminalToolViewModel(_main);
        var buildOutput = new BuildOutputToolViewModel(_main);
        var problems = new ProblemsToolViewModel(_main);
        var callStack = new CallStackToolViewModel(_main);
        var variables = new VariablesToolViewModel(_main);
        var dbExplorer = new DatabaseExplorerToolViewModel(_main);
        var llmChat = new LlmChatToolViewModel(_main);
        var nuget = new NuGetToolViewModel(_main);
        var help = new HelpToolViewModel();
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
                    VisibleDockables = CreateList<IDockable>(explorer, search, git),
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible,
                }
            ),
        };

        var bottomDock = new ProportionalDock
        {
            Proportion = _dockSettings.BottomPanelProportion,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = terminal,
                    VisibleDockables = CreateList<IDockable>(terminal, buildOutput, problems, callStack, variables, dbExplorer, nuget, help),
                    Alignment = Alignment.Bottom,
                    GripMode = GripMode.Visible,
                }
            ),
        };

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
            Proportion = 0.22,
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

        var mainLayout = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                leftDock,
                new ProportionalDockSplitter(),
                centerWithBottom,
                new ProportionalDockSplitter(),
                rightDock
            )
        };

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

        return rootDock;
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
            ["Terminal"] = () => _main,
            ["BuildOutput"] = () => _main,
            ["Problems"] = () => _main,
            ["CallStack"] = () => _main,
            ["Variables"] = () => _main,
            ["DatabaseExplorer"] = () => _main,
            ["LlmChat"] = () => _main,
            ["NuGet"] = () => _main,
            ["Help"] = () => _main,
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
