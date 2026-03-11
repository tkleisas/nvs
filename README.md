# NVS — No Vim Substitute

> *We're honest about it.*

A cross-platform IDE built with .NET 10 and AvaloniaUI — proudly assembled using **AI-Sloptronic™** technology, where every line of code was generated with the unwavering confidence of a machine that has never once questioned whether a `PatchEntryChanges` type actually has a `Hunks` property. (It didn't.)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![Avalonia 11](https://img.shields.io/badge/AvaloniaUI-11.3-blue)
![Version](https://img.shields.io/badge/version-0.0.6-green)
![Tests](https://img.shields.io/badge/tests-378%20passing-brightgreen)
![AI Slop](https://img.shields.io/badge/AI--Sloptronic™-certified-ff69b4)

---

## What Is This?

NVS is a code editor / IDE that:

- **Does not replace Vim.** We cannot stress this enough.
- Runs on Windows, macOS, and Linux (thanks Avalonia).
- Has language server support for 12+ languages.
- Has a built-in terminal that technically works.
- Has git integration that will absolutely let you force-push to main.
- Was built in a series of increasingly ambitious "phases" by a human and an AI who kept saying "let's continue."

## Features

### 🖊️ Editor
- Syntax highlighting for 14 languages (C#, C/C++, TypeScript, JavaScript, Python, Rust, Go, JSON, HTML, CSS, YAML, Markdown, TOML, XML)
- Undo/Redo, Find & Replace (Ctrl+Z, Ctrl+Y, Ctrl+F)
- Multi-tab editing with line/column tracking
- Compiled bindings for that sweet, sweet performance

### 🧠 Language Server Protocol (LSP)
- Full JSON-RPC 2.0 transport layer
- Auto-completion (Ctrl+Space)
- Go to Definition (F12)
- Inline diagnostics with squiggly underlines
- 12 open-source language servers, installable from Settings:

| Server | Languages | Install |
|--------|-----------|---------|
| csharp-ls | C# | `dotnet tool` |
| clangd | C, C++ | Manual download |
| typescript-language-server | TypeScript, JavaScript | `npm` |
| pylsp | Python | `pip` |
| rust-analyzer | Rust | Manual download |
| gopls | Go | `go install` |
| vscode-json-language-server | JSON | `npm` |
| vscode-html-language-server | HTML | `npm` |
| vscode-css-language-server | CSS/SCSS/LESS | `npm` |
| yaml-language-server | YAML | `npm` |
| marksman | Markdown | Manual download |
| taplo | TOML | `cargo` |

### 🔀 Git Integration
- Repository status, staging, unstaging
- Commit with message
- Branch management (create, checkout, list)
- Commit log
- Diff viewer with unified patch parsing
- Source Control sidebar panel

### 💻 Terminal
- Built-in terminal panel (Ctrl+`)
- Cross-platform shell detection (pwsh → PowerShell → cmd on Windows; `$SHELL` on Unix)
- Multiple terminal instances
- It's a Process with redirected I/O, not a real PTY — manage your expectations accordingly

### ⚙️ Settings
- 4-section settings UI (General, Editor, Language Servers, LLM)
- Language server discovery, status, and one-click install
- Settings persisted to `%APPDATA%/NVS/settings.json`

### 📁 Explorer
- File tree with type-specific icons (🟢 C# · 🔵 C++ · 🟡 JS · 🔷 TS · 🐍 Python · 🦀 Rust · and more)
- Compact indentation
- Double-click to open files

## Architecture

```
NVS (UI)  →  NVS.Services / NVS.Infrastructure  →  NVS.Core
                                                      NVS.Plugins
```

| Project | Role |
|---------|------|
| **NVS.Core** | Interfaces and models only. No implementations. The Switzerland of the codebase. |
| **NVS.Services** | All the actual work: Editor, FileSystem, Workspace, Language, LSP, Git, Terminal, Settings. |
| **NVS.Infrastructure** | DI registration, Serilog logging config. |
| **NVS.Plugins** | Plugin loading via `AssemblyLoadContext`. Currently quiet. Suspiciously quiet. |
| **NVS** | The Avalonia UI app — ViewModels, Views, Behaviors, and the DI composition root. |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git (for version stamping)

### Build & Run

```bash
# Build
dotnet build NVS.slnx

# Run
dotnet run --project src/NVS

# Run tests (378 of them)
dotnet test NVS.slnx
```

There is no separate lint command. Code style is enforced at build time via `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and the .NET analyzers. If it builds, it's "lint-passing." If it doesn't build, well, that's a different kind of feedback.

### Run a Single Test

```bash
dotnet test NVS.slnx --filter "FullyQualifiedName~GitServiceParsePatchTests.ParsePatch_MixedChanges"
```

## Tech Stack

| Component | Technology |
|-----------|------------|
| UI Framework | [AvaloniaUI](https://avaloniaui.net/) 11.3 |
| Text Editor | [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) 11.4 |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4 |
| Git | [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) 0.31 |
| Logging | [Serilog](https://serilog.net/) 4.2 |
| Runtime | .NET 10 (preview) |
| Theme | Fluent Dark (the only theme, because we have taste) |
| Testing | xUnit + FluentAssertions + NSubstitute |

## Testing

378 tests across 4 test projects:

- **NVS.Core.Tests** — Core model tests
- **NVS.Plugins.Tests** — Plugin system tests
- **NVS.Services.Tests** — EditorService, LanguageService, LSP, Git, Terminal, Registry, Manager tests
- **NVS.Tests** — ViewModel tests (Editor, Document, Settings, MainViewModel git/terminal)

Test naming convention: `MethodName_Scenario_ExpectedOutcome`

```csharp
[Fact]
public void ParsePatch_MixedChanges_ParsesAllLineTypes()
{
    // ...
}
```

## Versioning

- Version lives in `Directory.Build.props`
- Informational version appends the short git commit hash (e.g., `0.0.6+d4be659`)
- Patch bumps on each commit, minor bumps on feature completion
- Viewable in **Help → About**

## Contributing

This project is in early development. The interfaces in `NVS.Core` define about 18 services, of which ~10 are currently implemented. The remaining ones (Debug, Build, Index, KeyBindings, LLM, Theme) are waiting for someone brave enough — or another AI-Sloptronic™ session.

## License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  <i>NVS: Because the world needed another text editor, and we were too far in to stop.</i>
</p>
