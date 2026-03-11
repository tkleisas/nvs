using NVS.Core.Enums;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

/// <summary>
/// Static registry of known open-source language servers for each supported language.
/// </summary>
public static class LanguageServerRegistry
{
    private static readonly Dictionary<string, LanguageServerDefinition> Servers;
    private static readonly Dictionary<Language, string> LanguageToServerId;

    static LanguageServerRegistry()
    {
        var definitions = new LanguageServerDefinition[]
        {
            new()
            {
                Id = "csharp-ls",
                Name = "csharp-ls",
                Description = "Roslyn-based C# language server",
                License = "MIT",
                Languages = [Language.CSharp],
                BinaryName = "csharp-ls",
                DefaultArgs = [],
                InstallMethod = InstallMethod.DotnetTool,
                InstallCommand = "dotnet",
                InstallPackage = "csharp-ls",
                HomepageUrl = "https://github.com/razzmatazz/csharp-language-server",
            },
            new()
            {
                Id = "clangd",
                Name = "clangd",
                Description = "LLVM-based C/C++ language server",
                License = "Apache-2.0",
                Languages = [Language.C, Language.Cpp],
                BinaryName = "clangd",
                DefaultArgs = [],
                InstallMethod = InstallMethod.BinaryDownload,
                HomepageUrl = "https://clangd.llvm.org",
            },
            new()
            {
                Id = "typescript-language-server",
                Name = "TypeScript Language Server",
                Description = "Language server for TypeScript and JavaScript",
                License = "MIT",
                Languages = [Language.TypeScript, Language.JavaScript],
                BinaryName = "typescript-language-server",
                DefaultArgs = ["--stdio"],
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "typescript-language-server typescript",
                HomepageUrl = "https://github.com/typescript-language-server/typescript-language-server",
            },
            new()
            {
                Id = "pylsp",
                Name = "Python LSP Server",
                Description = "Community-maintained Python language server",
                License = "MIT",
                Languages = [Language.Python],
                BinaryName = "pylsp",
                DefaultArgs = [],
                InstallMethod = InstallMethod.Pip,
                InstallCommand = "pip",
                InstallPackage = "python-lsp-server",
                HomepageUrl = "https://github.com/python-lsp/python-lsp-server",
            },
            new()
            {
                Id = "rust-analyzer",
                Name = "rust-analyzer",
                Description = "Official Rust language server",
                License = "MIT OR Apache-2.0",
                Languages = [Language.Rust],
                BinaryName = "rust-analyzer",
                DefaultArgs = [],
                InstallMethod = InstallMethod.BinaryDownload,
                HomepageUrl = "https://rust-analyzer.github.io",
            },
            new()
            {
                Id = "gopls",
                Name = "gopls",
                Description = "Official Go language server",
                License = "BSD-3-Clause",
                Languages = [Language.Go],
                BinaryName = "gopls",
                DefaultArgs = ["serve"],
                InstallMethod = InstallMethod.GoInstall,
                InstallCommand = "go",
                InstallPackage = "golang.org/x/tools/gopls@latest",
                HomepageUrl = "https://pkg.go.dev/golang.org/x/tools/gopls",
            },
            new()
            {
                Id = "vscode-json-language-server",
                Name = "VSCode JSON Language Server",
                Description = "Language server for JSON with schema validation",
                License = "MIT",
                Languages = [Language.Json],
                BinaryName = "vscode-json-language-server",
                DefaultArgs = ["--stdio"],
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "vscode-langservers-extracted",
                HomepageUrl = "https://github.com/hrsh7th/vscode-langservers-extracted",
            },
            new()
            {
                Id = "vscode-html-language-server",
                Name = "VSCode HTML Language Server",
                Description = "Language server for HTML",
                License = "MIT",
                Languages = [Language.Html],
                BinaryName = "vscode-html-language-server",
                DefaultArgs = ["--stdio"],
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "vscode-langservers-extracted",
                HomepageUrl = "https://github.com/hrsh7th/vscode-langservers-extracted",
            },
            new()
            {
                Id = "vscode-css-language-server",
                Name = "VSCode CSS Language Server",
                Description = "Language server for CSS, SCSS, and LESS",
                License = "MIT",
                Languages = [Language.Css],
                BinaryName = "vscode-css-language-server",
                DefaultArgs = ["--stdio"],
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "vscode-langservers-extracted",
                HomepageUrl = "https://github.com/hrsh7th/vscode-langservers-extracted",
            },
            new()
            {
                Id = "yaml-language-server",
                Name = "YAML Language Server",
                Description = "Language server for YAML with schema support",
                License = "MIT",
                Languages = [Language.Yaml],
                BinaryName = "yaml-language-server",
                DefaultArgs = ["--stdio"],
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "yaml-language-server",
                HomepageUrl = "https://github.com/redhat-developer/yaml-language-server",
            },
            new()
            {
                Id = "marksman",
                Name = "Marksman",
                Description = "Markdown language server with wiki-link and reference support",
                License = "MIT",
                Languages = [Language.Markdown],
                BinaryName = "marksman",
                DefaultArgs = ["server"],
                InstallMethod = InstallMethod.BinaryDownload,
                HomepageUrl = "https://github.com/artempyanykh/marksman",
            },
            new()
            {
                Id = "taplo",
                Name = "Taplo",
                Description = "TOML language server and toolkit",
                License = "MIT",
                Languages = [Language.Toml],
                BinaryName = "taplo",
                DefaultArgs = ["lsp", "stdio"],
                InstallMethod = InstallMethod.Cargo,
                InstallCommand = "cargo",
                InstallPackage = "taplo-cli --features lsp",
                HomepageUrl = "https://taplo.tamasfe.dev",
            },
        };

        Servers = definitions.ToDictionary(d => d.Id);
        LanguageToServerId = new Dictionary<Language, string>();
        foreach (var def in definitions)
        {
            foreach (var lang in def.Languages)
            {
                LanguageToServerId.TryAdd(lang, def.Id);
            }
        }
    }

    public static IReadOnlyList<LanguageServerDefinition> GetAll() =>
        Servers.Values.ToList().AsReadOnly();

    public static LanguageServerDefinition? GetById(string serverId) =>
        Servers.GetValueOrDefault(serverId);

    public static LanguageServerDefinition? GetForLanguage(Language language) =>
        LanguageToServerId.TryGetValue(language, out var id) ? Servers[id] : null;

    public static IReadOnlyDictionary<string, LanguageServerDefinition> All => Servers;
}
