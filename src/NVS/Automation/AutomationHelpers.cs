using System.Windows.Input;
using Avalonia.Controls;

namespace NVS.Automation;

/// <summary>Reflection-based ICommand invocation used by the automation "command" endpoint.</summary>
internal static class CommandInvoker
{
    /// <summary>
    /// Invokes the ICommand stored in property <paramref name="name"/> (or
    /// <c>{name}Command</c>) on <paramref name="target"/>. Supports dotted paths
    /// (e.g. "Editor.CloseFileCommand") by walking intermediate properties.
    /// Returns false with a reason when the path does not resolve or CanExecute is false.
    /// </summary>
    public static bool TryInvoke(object target, string name, out string message)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var segments = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = target;

        for (var i = 0; i < segments.Length; i++)
        {
            var type = current.GetType();
            var isLast = i == segments.Length - 1;
            var property = type.GetProperty(segments[i])
                ?? (isLast ? type.GetProperty(segments[i] + "Command") : null);

            if (property is null)
            {
                message = $"no property '{segments[i]}'" + (isLast ? $" or '{segments[i]}Command'" : "") + $" on {type.Name}";
                return false;
            }

            var value = property.GetValue(current);

            if (!isLast)
            {
                if (value is null)
                {
                    message = $"property '{segments[i]}' on {type.Name} is null";
                    return false;
                }
                current = value;
                continue;
            }

            if (value is not ICommand command)
            {
                message = $"property '{segments[i]}' on {type.Name} is not an ICommand";
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

        message = "empty command path";
        return false;
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
