using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using NVS.ViewModels.Dock;
using NVS.Views.Dock;

namespace NVS;

public class DockViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> ViewMap = new()
    {
        [typeof(ExplorerToolViewModel)] = () => new ExplorerView(),
        [typeof(SearchToolViewModel)] = () => new SearchView(),
        [typeof(GitToolViewModel)] = () => new GitView(),
        [typeof(TerminalToolViewModel)] = () => new TerminalView(),
        [typeof(BuildOutputToolViewModel)] = () => new BuildOutputView(),
        [typeof(ProblemsToolViewModel)] = () => new ProblemsView(),
        [typeof(EditorDocumentViewModel)] = () => new EditorView(),
    };

    // Cache views by their ViewModel instance so tab-switching preserves state
    private readonly ConditionalWeakTable<object, Control> _viewCache = new();

    public Control? Build(object? data)
    {
        if (data is null) return null;

        if (_viewCache.TryGetValue(data, out var cached))
        {
            return cached;
        }

        if (ViewMap.TryGetValue(data.GetType(), out var factory))
        {
            var view = factory();
            _viewCache.AddOrUpdate(data, view);
            return view;
        }

        return new TextBlock { Text = $"No view for {data.GetType().Name}" };
    }

    public bool Match(object? data)
    {
        return data is IDockable;
    }
}
