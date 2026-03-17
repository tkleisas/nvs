using System;
using System.Collections.Generic;
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
        [typeof(CallStackToolViewModel)] = () => new CallStackView(),
        [typeof(VariablesToolViewModel)] = () => new VariablesView(),
        [typeof(EditorDocumentViewModel)] = () => new EditorView(),
        [typeof(DatabaseExplorerToolViewModel)] = () => new DatabaseExplorerView(),
        [typeof(LlmChatToolViewModel)] = () => new LlmChatView(),
        [typeof(NuGetToolViewModel)] = () => new NuGetView(),
        [typeof(WelcomeDocumentViewModel)] = () => new WelcomeView(),
        [typeof(HelpToolViewModel)] = () => new HelpView(),
        [typeof(CodeMetricsToolViewModel)] = () => new CodeMetricsView(),
        [typeof(DiffViewerToolViewModel)] = () => new DiffViewerView(),
        [typeof(ConflictResolverToolViewModel)] = () => new ConflictResolverView(),
    };

    public Control? Build(object? data)
    {
        if (data is null) return null;

        if (ViewMap.TryGetValue(data.GetType(), out var factory))
        {
            return factory();
        }

        return new TextBlock { Text = $"No view for {data.GetType().Name}" };
    }

    public bool Match(object? data)
    {
        return data is IDockable;
    }
}
