using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class VariablesToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private IDebugService? _debugService;

    public ObservableCollection<VariableNode> Variables { get; } = [];

    public VariablesToolViewModel(MainViewModel main)
    {
        _main = main;
        Id = "Variables";
        Title = "🔎 Variables";
        CanClose = false;
        CanPin = true;
    }

    public void SetDebugService(IDebugService? debugService)
    {
        _debugService = debugService;
    }

    public void UpdateVariables(IReadOnlyList<Variable> variables)
    {
        Variables.Clear();
        foreach (var v in variables)
        {
            var node = CreateNode(v);
            Variables.Add(node);
        }
    }

    public void ClearVariables()
    {
        Variables.Clear();
    }

    private VariableNode CreateNode(Variable v)
    {
        var hasChildren = v.VariablesReference.HasValue && v.VariablesReference > 0;
        var node = new VariableNode
        {
            Name = v.Name,
            Value = v.Value,
            Type = v.Type,
            HasChildren = hasChildren,
            VariablesReference = v.VariablesReference ?? 0,
        };

        // Add a placeholder child so the expand arrow shows
        if (hasChildren)
            node.Children.Add(VariableNode.LoadingPlaceholder);

        node.ExpandRequested += OnNodeExpandRequested;
        return node;
    }

    private async void OnNodeExpandRequested(object? sender, EventArgs e)
    {
        if (sender is not VariableNode node || node.VariablesReference <= 0 || _debugService is null)
            return;

        if (node.IsLoaded)
            return;

        try
        {
            var children = await _debugService.GetChildVariablesAsync(node.VariablesReference);
            node.Children.Clear();
            foreach (var child in children)
            {
                node.Children.Add(CreateNode(child));
            }
            node.IsLoaded = true;
        }
        catch
        {
            node.Children.Clear();
            node.Children.Add(new VariableNode
            {
                Name = "(error loading)",
                Value = "",
                Type = "",
            });
        }
    }
}

/// <summary>
/// Tree node representing a variable in the debug variables view.
/// Supports lazy-loading of child properties via ExpandRequested event.
/// </summary>
public sealed class VariableNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    public static VariableNode LoadingPlaceholder { get; } = new()
    {
        Name = "Loading...",
        Value = "",
        Type = "",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ExpandRequested;

    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Type { get; init; }
    public bool HasChildren { get; init; }
    public int VariablesReference { get; init; }
    public bool IsLoaded { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();

                // Request lazy-load of children on first expand
                if (value && !IsLoaded && HasChildren)
                    ExpandRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ObservableCollection<VariableNode> Children { get; } = [];

    public string Display => $"{Name} = {Value}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
