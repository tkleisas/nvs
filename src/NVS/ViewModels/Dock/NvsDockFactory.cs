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
    private IRootDock? _rootDock;
    private IDocumentDock? _documentDock;

    public NvsDockFactory(MainViewModel main)
    {
        _main = main;
    }

    public override IRootDock CreateLayout()
    {
        var explorer = new ExplorerToolViewModel(_main);
        var search = new SearchToolViewModel(_main);
        var git = new GitToolViewModel(_main);
        var terminal = new TerminalToolViewModel(_main);
        var editor = new EditorDocumentViewModel(_main);

        var leftDock = new ProportionalDock
        {
            Proportion = 0.22,
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
            Proportion = 0.25,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                new ToolDock
                {
                    ActiveDockable = terminal,
                    VisibleDockables = CreateList<IDockable>(terminal),
                    Alignment = Alignment.Bottom,
                    GripMode = GripMode.Visible,
                }
            ),
        };

        var documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            ActiveDockable = editor,
            VisibleDockables = CreateList<IDockable>(editor),
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

        var mainLayout = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>
            (
                leftDock,
                new ProportionalDockSplitter(),
                centerWithBottom
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
