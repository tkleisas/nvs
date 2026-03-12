using System.Collections.ObjectModel;
using System.ComponentModel;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;

namespace NVS.ViewModels.Dock;

public partial class VariablesToolViewModel : Tool
{
    private readonly MainViewModel _main;

    public ObservableCollection<VariableNode> Variables { get; } = [];

    public VariablesToolViewModel(MainViewModel main)
    {
        _main = main;
        Id = "Variables";
        Title = "🔎 Variables";
        CanClose = false;
        CanPin = true;
    }

    public void UpdateVariables(IReadOnlyList<Variable> variables)
    {
        Variables.Clear();
        foreach (var v in variables)
        {
            Variables.Add(new VariableNode
            {
                Name = v.Name,
                Value = v.Value,
                Type = v.Type,
                HasChildren = v.VariablesReference.HasValue && v.VariablesReference > 0,
                VariablesReference = v.VariablesReference ?? 0,
            });
        }
    }

    public void ClearVariables()
    {
        Variables.Clear();
    }
}

/// <summary>
/// Tree node representing a variable in the debug variables view.
/// </summary>
public sealed class VariableNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private ObservableCollection<VariableNode>? _children;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Type { get; init; }
    public bool HasChildren { get; init; }
    public int VariablesReference { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public ObservableCollection<VariableNode> Children
    {
        get => _children ??= [];
        set
        {
            _children = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Children)));
        }
    }

    public string Display => $"{Name} = {Value}";
}
