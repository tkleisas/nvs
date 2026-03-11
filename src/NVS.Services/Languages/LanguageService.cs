using NVS.Core.Enums;
using NVS.Core.Interfaces;

namespace NVS.Services.Languages;

public sealed class LanguageService : ILanguageService
{
    private static readonly Dictionary<Language, string[]> LanguageExtensions = new()
    {
        [Language.CSharp] = [".cs", ".csx"],
        [Language.C] = [".c", ".h"],
        [Language.Cpp] = [".cpp", ".cxx", ".cc", ".hpp", ".hxx", ".hh"],
        [Language.JavaScript] = [".js", ".jsx", ".mjs", ".cjs"],
        [Language.TypeScript] = [".ts", ".tsx", ".mts", ".cts"],
        [Language.Python] = [".py", ".pyw", ".pyi"],
        [Language.Rust] = [".rs"],
        [Language.Go] = [".go"],
        [Language.Json] = [".json"],
        [Language.Xml] = [".xml", ".xaml", ".axaml", ".csproj", ".fsproj", ".vbproj"],
        [Language.Markdown] = [".md", ".markdown"],
        [Language.Yaml] = [".yaml", ".yml"],
        [Language.Html] = [".html", ".htm"],
        [Language.Css] = [".css", ".scss", ".sass", ".less"],
        [Language.Toml] = [".toml"],
    };

    private static readonly Dictionary<Language, string> LanguageIds = new()
    {
        [Language.CSharp] = "csharp",
        [Language.C] = "c",
        [Language.Cpp] = "cpp",
        [Language.JavaScript] = "javascript",
        [Language.TypeScript] = "typescript",
        [Language.Python] = "python",
        [Language.Rust] = "rust",
        [Language.Go] = "go",
        [Language.Json] = "json",
        [Language.Xml] = "xml",
        [Language.Markdown] = "markdown",
        [Language.Yaml] = "yaml",
        [Language.Html] = "html",
        [Language.Css] = "css",
        [Language.Toml] = "toml",
        [Language.Unknown] = "plaintext",
    };

    private static readonly Dictionary<Language, string> LanguageServers = new()
    {
        [Language.CSharp] = "csharp-ls",
        [Language.C] = "clangd",
        [Language.Cpp] = "clangd",
        [Language.JavaScript] = "typescript-language-server",
        [Language.TypeScript] = "typescript-language-server",
        [Language.Python] = "pylsp",
        [Language.Rust] = "rust-analyzer",
        [Language.Go] = "gopls",
        [Language.Json] = "vscode-json-language-server",
        [Language.Html] = "vscode-html-language-server",
        [Language.Css] = "vscode-css-language-server",
        [Language.Yaml] = "yaml-language-server",
        [Language.Markdown] = "marksman",
        [Language.Toml] = "taplo",
    };

    public Language DetectLanguage(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        foreach (var (language, extensions) in LanguageExtensions)
        {
            if (extensions.Contains(extension))
            {
                return language;
            }
        }

        return Language.Unknown;
    }

    public string GetLanguageId(Language language)
    {
        return LanguageIds.TryGetValue(language, out var id) ? id : "plaintext";
    }

    public Language GetLanguageFromId(string languageId)
    {
        foreach (var (language, id) in LanguageIds)
        {
            if (string.Equals(id, languageId, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return Language.Unknown;
    }

    public IReadOnlyList<string> GetFileExtensions(Language language)
    {
        return LanguageExtensions.TryGetValue(language, out var extensions) 
            ? extensions 
            : Array.Empty<string>();
    }

    public string? GetLanguageServer(Language language)
    {
        return LanguageServers.TryGetValue(language, out var server) ? server : null;
    }
}
