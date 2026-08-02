using System.Windows.Input;
using Avalonia.Controls;

namespace NVS.Automation;

/// <summary>Reflection-based ICommand invocation used by the automation "command" endpoint.</summary>
internal static class CommandInvoker
{
    /// <summary>
    /// Invokes the ICommand stored in property <paramref name="name"/> (or
    /// <c>{name}Command</c>) on <paramref name="target"/>. Returns false with a
    /// reason when the property does not exist or CanExecute is false.
    /// </summary>
    public static bool TryInvoke(object target, string name, out string message)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var type = target.GetType();
        var property = type.GetProperty(name) ?? type.GetProperty(name + "Command");

        if (property?.GetValue(target) is not ICommand command)
        {
            message = $"no command property '{name}' or '{name}Command' on {type.Name}";
            return false;
        }

        if (!command.CanExecute(null))
        {
            message = $"{property.Name}.CanExecute returned false";
            return false;
        }

        command.Execute(null);
        message = $"executed {property.Name}";
        return true;
    }
}

/// <summary>Menu item lookup by underscore-insensitive header path for the "menu" endpoint.</summary>
internal static class MenuItemMatcher
{
    /// <summary>
    /// Finds a menu item by path (e.g. "Database/Ask AI...") within the given root items
    /// (typically the main Menu's Items). Matching ignores '_' access-key markers and case.
    /// </summary>
    public static MenuItem? Find(IEnumerable<object?> items, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var current = items;
        foreach (var segment in segments)
        {
            var match = current?
                .OfType<MenuItem>()
                .FirstOrDefault(item => string.Equals(Normalize(item.Header), segment, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return null;
            }

            if (segment == segments[^1])
            {
                return match;
            }

            current = match.Items;
        }

        return null;
    }

    private static string? Normalize(object? header) =>
        header?.ToString()?.Replace("_", string.Empty);
}
