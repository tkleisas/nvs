using System.Xml;
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

        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>
    /// Clears the cache, forcing definitions to be reloaded on next access.
    /// Useful for testing.
    /// </summary>
    internal static void ClearCache() => Cache.Clear();
}
