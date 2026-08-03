using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using NVS.ViewModels;

namespace NVS.Automation;

/// <summary>
/// <see cref="IAutomationHost"/> over the real <see cref="MainWindow"/>/<see cref="MainViewModel"/>.
/// All UI work is marshaled onto the Avalonia UI thread.
/// </summary>
public sealed class MainWindowAutomationHost : IAutomationHost
{
    private readonly MainWindow _window;
    private readonly MainViewModel _vm;

    public MainWindowAutomationHost(MainWindow window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
    }

    public Task<object> PingAsync()
    {
        object result = new Dictionary<string, object?>
        {
            ["app"] = "NVS",
            ["version"] = AppVersionInfo.InformationalVersion,
        };
        return Task.FromResult(result);
    }

    public async Task<object> GetStateAsync() =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var documents = new List<Dictionary<string, object?>>();
            string? activeDocument = null;
            if (_vm.DockLayout is not null)
            {
                CollectDockables(_vm.DockLayout, documents);
                var documentDock = FindDocumentDock(_vm.DockLayout);
                activeDocument = documentDock?.ActiveDockable?.Id;
            }

            return (object)new Dictionary<string, object?>
            {
                ["title"] = _vm.Title,
                ["workspacePath"] = _vm.WorkspacePath,
                ["isWorkspaceOpen"] = _vm.IsWorkspaceOpen,
                ["statusMessage"] = _vm.StatusMessage,
                ["activeDocument"] = activeDocument,
                ["dockables"] = documents,
            };
        });

    public async Task<object> GetTreeAsync(int maxDepth, int maxNodes, string? controlId = null) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var budget = new NodeBudget(maxNodes);

            if (!string.IsNullOrWhiteSpace(controlId))
            {
                var control = FindControl(controlId)
                    ?? throw new InvalidOperationException($"no control with automation id or name '{controlId}'");
                var root = DescribeVisual(control, depth: 0, maxDepth, budget, isWindow: false);
                return (object)new Dictionary<string, object?>
                {
                    ["nodeCount"] = budget.Count,
                    ["truncated"] = budget.Exhausted,
                    ["root"] = root,
                };
            }

            var windows = new List<Dictionary<string, object?>>();
            foreach (var window in GetWindows())
            {
                windows.Add(DescribeVisual(window, depth: 0, maxDepth, budget, isWindow: true));
                if (budget.Exhausted) break;
            }
            return (object)new Dictionary<string, object?>
            {
                ["nodeCount"] = budget.Count,
                ["truncated"] = budget.Exhausted,
                ["windows"] = windows,
            };
        });

    public async Task<object> ScreenshotAsync(string path, string? controlId) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Visual target = _window;

            if (!string.IsNullOrWhiteSpace(controlId))
            {
                var found = FindControl(controlId)
                    ?? throw new InvalidOperationException($"no control with automation id or name '{controlId}'");
                target = found;
            }

            return RenderToPng(target, path);
        });

    public async Task<object> ScreenshotWindowAsync(string path, string title) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var target = GetWindows().FirstOrDefault(w =>
                    w.Title?.Contains(title, StringComparison.OrdinalIgnoreCase) == true)
                ?? throw new InvalidOperationException(
                    $"no window with title containing '{title}' (open: {string.Join(", ", GetWindows().Select(w => w.Title))})");

            return RenderToPng(target, path);
        });

    private static object RenderToPng(Visual target, string path)
    {
        // Flush pending layout/render work so the capture reflects the latest state.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

        var width = (int)Math.Ceiling(target.Bounds.Width);
        var height = (int)Math.Ceiling(target.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"target has empty bounds ({width}x{height}) — is it visible?");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(target);
        bitmap.Save(path);

        return new Dictionary<string, object?>
        {
            ["path"] = Path.GetFullPath(path),
            ["width"] = width,
            ["height"] = height,
        };
    }

    public async Task<object> InvokeCommandAsync(string name) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!CommandInvoker.TryInvoke(_vm, name, out var message))
            {
                throw new InvalidOperationException(message);
            }
            return (object)new Dictionary<string, object?> { ["invoked"] = name, ["detail"] = message };
        });

    public async Task<object> InvokeMenuAsync(string path) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var menu = _window.GetVisualDescendants().OfType<Menu>().FirstOrDefault()
                ?? throw new InvalidOperationException("no Menu found in main window");

            var item = MenuItemMatcher.Find(menu.Items, path)
                ?? throw new InvalidOperationException($"menu item not found: '{path}'");

            // Raising Click drives both Command-bound and Click-handler menu items.
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            return (object)new Dictionary<string, object?> { ["invoked"] = path };
        });

    public async Task<object> SetTextAsync(string controlId, string text) =>
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var control = FindControl(controlId)
                ?? throw new InvalidOperationException($"no control with automation id or name '{controlId}'");

            switch (control)
            {
                case AvaloniaEdit.TextEditor editor:
                    editor.Document.Text = text;
                    break;
                case TextBox textBox:
                    textBox.Text = text;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"control '{controlId}' is a {control.GetType().Name}; expected a TextEditor or TextBox");
            }

            return (object)new Dictionary<string, object?> { ["control"] = controlId, ["length"] = text.Length };
        });

    public async Task<object> OpenSolutionAsync(string path) =>
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await _vm.OpenSolutionFromPathAsync(path);
            return (object)new Dictionary<string, object?>
            {
                ["opened"] = path,
                ["statusMessage"] = _vm.StatusMessage,
            };
        });

    public async Task<object> OpenFileAsync(string path) =>
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await _vm.OpenFileAsync(path);
            return (object)new Dictionary<string, object?>
            {
                ["opened"] = path,
                ["statusMessage"] = _vm.StatusMessage,
            };
        });

    public async Task<object> ActivateAsync(string id)
    {
        if (id.Equals("editor", StringComparison.OrdinalIgnoreCase))
        {
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _vm.ActivateEditorDocument();
                return (object)new Dictionary<string, object?> { ["invoked"] = "editor" };
            });
        }

        var commandName = id.ToLowerInvariant() switch
        {
            "databaseexplorer" or "database" => "ShowDatabaseExplorer",
            "apiclient" or "api" => "ShowApiClient",
            "welcome" => "ShowWelcome",
            "help" => "ShowHelp",
            "codemetrics" => "ShowCodeMetrics",
            "explorer" => "ShowExplorer",
            "search" => "ShowSearch",
            "git" or "sourcecontrol" => "ShowSourceControl",
            _ => throw new InvalidOperationException(
                $"unknown panel '{id}' (known: DatabaseExplorer, ApiClient, Welcome, Help, CodeMetrics, Explorer, Search, Git, Editor)"),
        };

        return await InvokeCommandAsync(commandName);
    }

    private IEnumerable<Window> GetWindows()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                yield return window;
            }
        }
    }

    private Visual? FindControl(string idOrName)
    {
        // Dock panels may keep detached/cached view instances around with stale
        // content — always prefer the effectively visible match.
        Visual? hiddenFallback = null;
        foreach (var window in GetWindows())
        {
            foreach (var control in window.GetVisualDescendants().OfType<Control>())
            {
                var matches =
                    string.Equals(AutomationProperties.GetAutomationId(control), idOrName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(control.Name, idOrName, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }

                if (control.IsEffectivelyVisible)
                {
                    return control;
                }

                hiddenFallback ??= control;
            }
        }
        return hiddenFallback;
    }

    private static void CollectDockables(IDockable dockable, List<Dictionary<string, object?>> output)
    {
        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                CollectDockables(child, output);
            }
        }
        else
        {
            output.Add(new Dictionary<string, object?>
            {
                ["id"] = dockable.Id,
                ["title"] = dockable.Title,
            });
        }
    }

    private static IDocumentDock? FindDocumentDock(IDockable dockable)
    {
        if (dockable is IDocumentDock documentDock)
        {
            return documentDock;
        }
        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = FindDocumentDock(child);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private static Dictionary<string, object?> DescribeVisual(Visual visual, int depth, int maxDepth, NodeBudget budget, bool isWindow = false)
    {
        budget.Count++;
        var node = new Dictionary<string, object?>
        {
            ["type"] = visual.GetType().Name,
            ["name"] = visual is Control control ? control.Name : null,
            ["automationId"] = visual is Control c2 ? AutomationProperties.GetAutomationId(c2) : null,
            ["isVisible"] = visual is Control c3 ? c3.IsVisible : null,
            ["bounds"] = $"{visual.Bounds.Width:F0}x{visual.Bounds.Height:F0}",
            ["text"] = GetNodeText(visual),
        };
        if (isWindow && visual is Window window)
        {
            node["title"] = window.Title;
        }

        if (depth < maxDepth && !budget.Exhausted)
        {
            var children = new List<Dictionary<string, object?>>();
            foreach (var child in EnumerateChildren(visual))
            {
                children.Add(DescribeVisual(child, depth + 1, maxDepth, budget));
                if (budget.Exhausted) break;
            }
            if (children.Count > 0)
            {
                node["children"] = children;
            }
        }

        return node;
    }

    private static string? GetNodeText(Visual visual) => visual switch
    {
        TextBlock textBlock => textBlock.Text,
        Button button => button.Content?.ToString(),
        MenuItem menuItem => menuItem.Header?.ToString(),
        TabItem tabItem => tabItem.Header?.ToString(),
        _ => null,
    };

    private static IEnumerable<Visual> EnumerateChildren(Visual visual)
    {
        var seen = new HashSet<Visual>();
        foreach (var child in visual.GetVisualChildren())
        {
            if (seen.Add(child))
            {
                yield return child;
            }
        }

        // Content presenters expose their content through the logical tree.
        if (visual is global::Avalonia.LogicalTree.ILogical logical)
        {
            foreach (var child in logical.LogicalChildren.OfType<Visual>())
            {
                if (seen.Add(child))
                {
                    yield return child;
                }
            }
        }
    }

    private sealed class NodeBudget
    {
        private readonly int _max;
        public int Count;
        public NodeBudget(int max) => _max = max;
        public bool Exhausted => Count >= _max;
    }
}
