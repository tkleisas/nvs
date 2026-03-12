# NVS — No Vim Substitute

> *We're honest about it.*

A cross-platform IDE built with .NET 10 and AvaloniaUI — proudly assembled using **AI-Sloptronic™** technology, where every line of code was generated with the unwavering confidence of a machine that has never once questioned whether a `PatchEntryChanges` type actually has a `Hunks` property. (It didn't.)

[![Build](https://github.com/tkleisas/nvs/actions/workflows/build.yml/badge.svg)](https://github.com/tkleisas/nvs/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)
![Avalonia 11](https://img.shields.io/badge/AvaloniaUI-11.3-blue)
![Version](https://img.shields.io/badge/version-0.1.4-green)
![Tests](https://img.shields.io/badge/tests-535%20passing-brightgreen)
![AI Slop](https://img.shields.io/badge/AI--Sloptronic™-certified-ff69b4)

---

## What Is This?

NVS is a code editor / IDE that:

- **Does not replace Vim.** We cannot stress this enough.
- Runs on Windows, macOS, and Linux (thanks Avalonia).
- Has language server support for 12+ languages with auto-completion and signature help.
- Has a built-in PTY terminal that actually works.
- Has git integration that will absolutely let you force-push to main.
- Has .NET solution/project loading with full build integration.
- Has DAP-based debugging with breakpoints, call stack, and variable inspection.
- Was built in a series of increasingly ambitious "phases" by a human and an AI who kept saying "let's continue."

## Features

### 🖊️ Editor
- Syntax highlighting for 14 languages (C#, C/C++, TypeScript, JavaScript, Python, Rust, Go, JSON, HTML, CSS, YAML, Markdown, TOML, XML)
- Undo/Redo, Find & Replace (Ctrl+Z, Ctrl+Y, Ctrl+F)
- Multi-tab editing with close buttons, line/column tracking
- Right-click context menu (Cut, Copy, Paste, Select All, Go to Definition)
- Dockable panels via Dock.Avalonia — drag, split, and rearrange
- Compiled bindings for that sweet, sweet performance

### 🧠 Language Server Protocol (LSP)
- Full JSON-RPC 2.0 transport layer
- Auto-completion with trigger characters (`.`, `<`, `:`) and debounced identifier completion
- Signature help / parameter info on `(` and `,`
- Go to Definition (F12)
- Inline diagnostics with squiggly underlines
- Incremental document sync (`textDocument/didChange`)
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
- Built-in PTY terminal panel (Ctrl+`) via Iciclecreek
- Cross-platform shell detection (pwsh → PowerShell → cmd on Windows; `$SHELL` on Unix)
- Multiple terminal instances
- Configurable fonts (MesloLGM Nerd Font etc.)

### 🏗️ Solution & Build
- Open .sln, .slnx, and .csproj files
- Solution Explorer tree with project structure
- Build, Rebuild, Clean (Ctrl+Shift+B)
- Run without debugging (Ctrl+F5)
- Build Output panel with auto-scroll and MSBuild error parsing
- Problems panel with click-to-navigate diagnostics

### 🐛 Debugging (DAP)
- Debug Adapter Protocol client with Content-Length framed transport
- **netcoredbg** auto-downloaded on first use (~3 MB) — no manual install needed
- Start Debugging (F5), Stop (Shift+F5)
- Step Over (F10), Step Into (F11), Step Out (Shift+F11)
- Toggle Breakpoint (F9) with red gutter markers
- Call Stack panel with frame navigation
- Variables panel with expandable tree view (lazy-loaded children)
- Debug output streamed to Build Output panel
- Debug toolbar with visual step controls

### ⚙️ Settings
- 4-section settings UI (General, Editor, Language Servers, LLM)
- Language server discovery, status, and one-click install
- Window state persistence (size, position, maximized)
- Settings persisted to `%APPDATA%/NVS/settings.json`

### 📁 Explorer
- File tree with type-specific icons (🟢 C# · 🔵 C++ · 🟡 JS · 🔷 TS · 🐍 Python · 🦀 Rust · and more)
- Compact indentation with expand/collapse state
- Double-click to open files

## Architecture

```
NVS (UI)  →  NVS.Services / NVS.Infrastructure  →  NVS.Core
                                                      NVS.Plugins
```

| Project | Role |
|---------|------|
| **NVS.Core** | Interfaces and models only. No implementations. The Switzerland of the codebase. |
| **NVS.Services** | All the actual work: Editor, FileSystem, Workspace, Language, LSP, Git, Terminal, Settings, Solution, Build, Debug. |
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

# Run tests (535 of them)
dotnet test NVS.slnx
```

There is no separate lint command. Code style is enforced at build time via `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and the .NET analyzers. If it builds, it's "lint-passing." If it doesn't build, well, that's a different kind of feedback.

### Run a Single Test

```bash
dotnet test NVS.slnx --filter "FullyQualifiedName~GitServiceParsePatchTests.ParsePatch_MixedChanges"
```

## Downloads

Self-contained builds (no .NET runtime needed) are published on each tagged release:

| Platform | Archive |
|----------|---------|
| Windows x64 | `nvs-win-x64.zip` |
| Linux x64 | `nvs-linux-x64.tar.gz` |
| macOS x64 | `nvs-osx-x64.tar.gz` |
| macOS ARM64 | `nvs-osx-arm64.tar.gz` |

See [Releases](https://github.com/tkleisas/nvs/releases) for downloads.

To create a release, tag a commit and push:

```bash
git tag v0.1.4
git push origin v0.1.4
```

## Tech Stack

| Component | Technology |
|-----------|------------|
| UI Framework | [AvaloniaUI](https://avaloniaui.net/) 11.3 |
| Text Editor | [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) 11.4 |
| Docking | [Dock.Avalonia](https://github.com/wieslawsoltes/Dock) 11.3 |
| Terminal | [Iciclecreek.Avalonia.Terminal](https://github.com/tomlm/Iciclecreek.Avalonia.Terminal) 1.0 |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4 |
| Git | [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) 0.31 |
| Debugging | [netcoredbg](https://github.com/Samsung/netcoredbg) (MIT, auto-downloaded) |
| Logging | [Serilog](https://serilog.net/) 4.2 |
| Runtime | .NET 10 (preview) |
| Theme | Fluent Dark (the only theme, because we have taste) |
| Testing | xUnit + FluentAssertions + NSubstitute |

## Testing

535 tests across 4 test projects:

- **NVS.Core.Tests** — Core model tests
- **NVS.Plugins.Tests** — Plugin system tests
- **NVS.Services.Tests** — EditorService, LanguageService, LSP, Git, Terminal, Registry, Solution, Build, DAP, Debug, Breakpoints
- **NVS.Tests** — ViewModel tests (Editor, Document, Settings, MainViewModel, Build/Run commands)

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
- Informational version appends the short git commit hash (e.g., `0.1.4+a3f72b1`)
- Patch bumps on each commit, minor bumps on feature completion
- Viewable in **Help → About**

## Contributing

This project is in active development. Current focus areas:

- 🔲 Project & file templates (dotnet new integration)
- 🔲 SQLite database explorer
- 🔲 Help system for beginners
- 🔲 LLM integration

## License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  <i>NVS: Because the world needed another text editor, and we were too far in to stop.</i>
</p>
