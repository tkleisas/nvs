using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Automation;

namespace NVS.ViewModels;

/// <summary>One executable entry in the command palette.</summary>
public sealed record CommandPaletteItem(string Title, string CommandName);

/// <summary>
/// Backs the Ctrl+Shift+P command palette: a fuzzy-filtered, keyboard-driven
/// list of every command exposed by the main view model and its sub-view models
/// (Editor, Terminal, Git, BuildRun, Debug, Explorer, Search).
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private static readonly string[] SubViewModels =
        ["Editor", "Terminal", "Git", "BuildRun", "Debug", "Explorer", "Search"];

    private readonly MainViewModel _main;
    private readonly List<CommandPaletteItem> _allItems = new();

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    [ObservableProperty]
    private bool _isOpen;

    public CommandPaletteViewModel(MainViewModel main)
    {
        _main = main;
        CollectCommands(main, prefix: null);
        foreach (var sub in SubViewModels)
        {
            var value = main.GetType().GetProperty(sub)?.GetValue(main);
            if (value is not null)
            {
                CollectCommands(value, prefix: sub);
            }
        }

        _allItems.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
    }

    public ObservableCollection<CommandPaletteItem> Items { get; } = new();

    public void Open()
    {
        Query = string.Empty;
        RefreshItems();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>Executes the selected command and closes the palette.</summary>
    public void ExecuteSelected()
    {
        if (SelectedItem is null) return;

        var commandName = SelectedItem.CommandName;
        Close();
        if (!CommandInvoker.TryInvoke(_main, commandName, out var message))
        {
            _main.StatusMessage = message;
        }
    }

    /// <summary>Moves the selection by <paramref name="delta"/> within the filtered list.</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0) return;

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        index = Math.Clamp(index + delta, 0, Items.Count - 1);
        SelectedItem = Items[index];
    }

    partial void OnQueryChanged(string value)
    {
        RefreshItems();
    }

    private void RefreshItems()
    {
        Items.Clear();
        foreach (var item in _allItems.Where(Matches))
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
    }

    private bool Matches(CommandPaletteItem item)
    {
        if (string.IsNullOrWhiteSpace(Query)) return true;

        return Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(term => item.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void CollectCommands(object target, string? prefix)
    {
        foreach (var property in target.GetType().GetProperties())
        {
            if (!typeof(ICommand).IsAssignableFrom(property.PropertyType)) continue;
            if (property.GetValue(target) is not ICommand) continue;

            var name = property.Name;
            if (!name.EndsWith("Command", StringComparison.Ordinal)) continue;

            var baseName = name[..^"Command".Length];
            var title = prefix is null
                ? Humanize(baseName)
                : $"{prefix}: {Humanize(baseName)}";
            var commandName = prefix is null ? name : $"{prefix}.{name}";
            _allItems.Add(new CommandPaletteItem(title, commandName));
        }
    }

    internal static string Humanize(string name)
    {
        var chars = new List<char>();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && chars.Count > 0 && !char.IsUpper(chars[^1]))
            {
                chars.Add(' ');
            }
            chars.Add(c);
        }
        return new string(chars.ToArray());
    }
}
