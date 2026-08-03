using System.Xml;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using NVS.Core.Enums;

namespace NVS.Highlighting;

public static class SyntaxHighlightingLoader
{
    private static readonly Dictionary<Language, IHighlightingDefinition?> Cache = new();

    private static readonly Dictionary<Language, string> ResourceNames = new()
    {
        [Language.CSharp] = "CSharp",
        [Language.C] = "C",
        [Language.Cpp] = "Cpp",
        [Language.JavaScript] = "JavaScript",
        [Language.TypeScript] = "TypeScript",
        [Language.Python] = "Python",
        [Language.Rust] = "Rust",
        [Language.Go] = "Go",
        [Language.Json] = "Json",
        [Language.Xml] = "Xml",
        [Language.Html] = "Html",
        [Language.Css] = "Css",
        [Language.Markdown] = "Markdown",
        [Language.Yaml] = "Yaml",
        [Language.Toml] = "Toml",
        [Language.Sql] = "Sql",
        [Language.Java] = "Java",
        [Language.Php] = "Php",
    };

    public static IHighlightingDefinition? GetHighlighting(Language language)
    {
        if (Cache.TryGetValue(language, out var cached))
            return cached;

        var definition = LoadFromXshd(language);
        Cache[language] = definition;
        return definition;
    }

    internal static IHighlightingDefinition? LoadFromXshd(Language language)
    {
        if (!ResourceNames.TryGetValue(language, out var name))
            return null;

        var resourceName = $"NVS.Highlighting.Definitions.{name}.xshd";
        var assembly = typeof(SyntaxHighlightingLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        // Recolor the VS Dark+ palette to stay readable on the active theme's
        // editor background (critical for the light theme).
        var doc = new XmlDocument();
        doc.Load(stream);
        HighlightingRecolorer.RecolorForBackground(doc, CurrentEditorBackground());

        using var reader = new XmlNodeReader(doc);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private static Color CurrentEditorBackground()
    {
        if (Avalonia.Application.Current?.Resources.TryGetValue("EditorBackgroundBrush", out var value) == true
            && value is Avalonia.Media.SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromRgb(0x1E, 0x1E, 0x1E);
    }

    /// <summary>Raised after <see cref="InvalidateCache"/> so views can re-apply highlighting.</summary>
    public static event EventHandler? CacheInvalidated;

    /// <summary>
    /// Clears the cache and notifies subscribers (called on theme change so open
    /// editors re-apply highlighting recolored for the new background).
    /// </summary>
    public static void InvalidateCache()
    {
        ClearCache();
        CacheInvalidated?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the cache, forcing definitions to be reloaded on next access.
    /// Useful for testing.
    /// </summary>
    internal static void ClearCache() => Cache.Clear();
}
