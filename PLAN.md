# NVS - No Vim Substitute

> "We're honest"

A cross-platform open-source IDE built with .NET 10 and AvaloniaUI.

## Current Status: v0.3.0 — 806 tests passing

All phases 1–8 complete. Multi-project solutions, startup project selection, CLI arguments, GUI app launching, and code metrics all working.

## Technology Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 10 |
| UI Framework | AvaloniaUI 11.x |
| MVVM | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| Logging | Serilog |
| Testing | xUnit + FluentAssertions + NSubstitute |
| Git | LibGit2Sharp |
| Editor | AvaloniaEdit |
| LSP/DAP | Custom JSON-RPC via System.IO.Pipelines |
| LLM | OpenAI-compatible API |
| Code Metrics | Microsoft.CodeAnalysis (Roslyn) |
| Terminal | Iciclecreek.Avalonia.Terminal |
| Debugging | netcoredbg (auto-downloaded) |

## Architecture Overview

```
+-----------------------------------------------------------------+
|                         NVS IDE                                  |
+-----------------------------------------------------------------+
|  UI Layer (AvaloniaUI - MVVM)                                   |
|  +-- EditorShell    +-- Sidebar    +-- StatusBar    +-- Tabs    |
+-----------------------------------------------------------------+
|  Core Services (Interfaces + Implementations)                   |
|  +-- IEditorService  +-- ILspClient  +-- IDebugService          |
|  +-- IGitService     +-- ITerminalService  +-- IIndexService    |
|  +-- IBuildService   +-- IPluginManager  +-- ILLMService        |
|  +-- ICodeMetricsService  +-- ISolutionService                  |
+-----------------------------------------------------------------+
|  Infrastructure                                                  |
|  +-- Plugin Engine  +-- LSP/DAP Clients  +-- Process Manager    |
+-----------------------------------------------------------------+
```

## Solution Structure

```
NVS/
+-- src/
|   +-- NVS.Core/                    # Interfaces, abstractions, models
|   +-- NVS.Infrastructure/          # Cross-cutting concerns (DI, logging)
|   +-- NVS.Services/                # All service implementations
|   +-- NVS.Plugins/                 # Plugin loading via AssemblyLoadContext
|   +-- NVS/                         # Avalonia UI application
+-- tests/
|   +-- NVS.Core.Tests/
|   +-- NVS.Services.Tests/          # 548 tests
|   +-- NVS.Plugins.Tests/
|   +-- NVS.Tests/                   # 256 ViewModel tests
+-- lib/
|   +-- SQLiteExplorer/              # SQLite browser component
+-- tools/
|   +-- DebugStartupHook/            # Console app debug attach hook
+-- NVS.slnx
+-- Directory.Build.props
```

## Implementation Phases

### Phase 1: Foundation ✅
- [x] Solution setup with .NET 10, AvaloniaUI
- [x] Core interfaces and DI setup
- [x] Basic shell UI (window, layout, menus)
- [x] Project/workspace model
- [x] Settings infrastructure

### Phase 2: Editor Core ✅
- [x] AvaloniaEdit integration with syntax highlighting (14 languages)
- [x] Multi-tab editing with dirty indicators
- [x] Undo/Redo, Find & Replace
- [x] File tree with type-specific icons
- [x] Dockable panels via Dock.Avalonia

### Phase 3: LSP Integration ✅
- [x] Full JSON-RPC 2.0 transport layer
- [x] Auto-completion, signature help, go-to-definition
- [x] Inline diagnostics with squiggly underlines
- [x] Code Actions / Quick Fixes (Ctrl+.)
- [x] 12 language server configurations with one-click install

### Phase 4: Git + Terminal ✅
- [x] LibGit2Sharp integration (status, staging, commit, diff)
- [x] Branch management (create, checkout, delete)
- [x] PTY terminal with cross-platform shell detection
- [x] Multiple terminal instances

### Phase 5: Debugging (DAP) ✅
- [x] DAP client with Content-Length framed transport
- [x] netcoredbg auto-download
- [x] Breakpoints, call stack, variables panel
- [x] Console apps via terminal, GUI apps via DAP launch

### Phase 6: Solution & Build ✅
- [x] .sln/.slnx/.csproj loading with multi-project support
- [x] Build/Rebuild/Clean with MSBuild error parsing
- [x] Run with GUI app detection (WinExe → detached process)
- [x] Startup project selection (dropdown + context menu)
- [x] New project scaffolding, add project to solution

### Phase 7: Plugins, LLM, NuGet, SQLite, Help ✅
- [x] Plugin loading via AssemblyLoadContext
- [x] LLM chat with agent tools (file R/W, search, terminal, editor)
- [x] NuGet package manager (browse, install, update, uninstall)
- [x] SQLite database explorer
- [x] Help system with F1 search and Welcome tab

### Phase 8: Code Metrics & Linting ✅
- [x] 8.1 — LSP Code Actions with Quick Fix UI
- [x] 8.2 — Roslyn-based code metrics service
- [x] 8.3 — Code Metrics Dashboard panel
- [x] 8.4 — Inline metrics gutter dots and status bar display

### Post-Phase Improvements ✅
- [x] Multi-project solution tree (root-level project exclusion fix)
- [x] Startup project selection (toolbar + explorer context menu)
- [x] GUI app launching as detached process
- [x] CLI argument support (`nvs solution.sln`, `nvs ./folder/`, `nvs file.cs`)

### Future Ideas
- [ ] Docker/Podman integration
- [ ] Remote development (SSH)
- [ ] Extension marketplace
- [ ] Vim keybinding mode (the irony)
- [ ] Light theme (heresy)
- [ ] Collaborative editing

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Config Format | JSON | Native .NET, simple, type-safe |
| Workspace Model | Hybrid | Folder-based + solution-based |
| Theming | Fluent Dark only | Consistent, tasteful |
| Telemetry | None | Privacy-focused |
| LLM Integration | OpenAI-compatible | Supports Ollama, OpenRouter, LM Studio, etc. |
| Terminal | Iciclecreek PTY | Native cross-platform PTY |
| Plugin API | AssemblyLoadContext | Isolation + full API access |
| Code Metrics | Roslyn | Native C# analysis, no external tools |
