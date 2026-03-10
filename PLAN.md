# NVS - No Vim Substitute

> "We're honest"

A cross-platform open-source IDE built with .NET 10 and AvaloniaUI.

## Technology Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 10 |
| UI Framework | AvaloniaUI 11.x |
| MVVM | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| ORM | Dapper |
| Database | SQLite (per-workspace) |
| Logging | Serilog |
| Testing | xUnit |
| Git | LibGit2Sharp |
| Editor | AvaloniaEdit |
| LSP/DAP | Custom JSON-RPC via System.IO.Pipelines |
| LLM | OpenAI-compatible API |
| File Watching | System.IO.FileSystemWatcher |
| Async | Task-based |

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
+-----------------------------------------------------------------+
|  Infrastructure                                                  |
|  +-- Plugin Engine  +-- SQLite Indexer  +-- LSP/DAP Clients     |
|  +-- Process Manager  +-- File System Watcher                   |
+-----------------------------------------------------------------+
|  Data Layer                                                      |
|  +-- Project Index (SQLite)  +-- Settings  +-- Plugin State     |
+-----------------------------------------------------------------+
```

## Solution Structure

```
NVS/
+-- src/
|   +-- NVS.Core/                    # Interfaces, abstractions, models
|   |   +-- Models/                  # Project, Document, Workspace, Symbol
|   |   +-- Interfaces/              # IService contracts
|   |   +-- Events/                  # Domain events
|   |
|   +-- NVS.Infrastructure/          # Cross-cutting concerns
|   |   +-- Logging/                 # Serilog config
|   |   +-- DependencyInjection/     # Service registration
|   |   +-- Configuration/           # Settings management
|   |
|   +-- NVS.Services/
|   |   +-- Editor/                  # AvaloniaEdit wrapper, highlighting
|   |   +-- Lsp/                     # LSP client, server management
|   |   +-- Debug/                   # DAP client, debug adapters
|   |   +-- Git/                     # LibGit2Sharp wrapper
|   |   +-- Terminal/                # Terminal integration
|   |   +-- Index/                   # SQLite indexing, search
|   |   +-- Build/                   # Build system integration
|   |   +-- Llm/                     # OpenAI-compatible client
|   |
|   +-- NVS.Plugins/
|   |   +-- Abstractions/            # Plugin API interfaces
|   |   +-- Engine/                  # Loading, lifecycle
|   |   +-- BuiltIn/                 # Core plugins (optional)
|   |
|   +-- NVS/                         # Avalonia UI application
|       +-- Views/                   # Windows, controls
|       +-- ViewModels/              # MVVM viewmodels
|       +-- Themes/                  # Theme definitions
|       +-- Assets/                  # Icons, fonts
|
+-- tests/
|   +-- NVS.Core.Tests/
|   +-- NVS.Services.Tests/
|   +-- NVS.Plugins.Tests/
|
+-- NVS.sln
+-- Directory.Build.props
+-- .github/
|   +-- workflows/
|       +-- build.yml
|       +-- release.yml
+-- README.md
```

## Core Interfaces

```csharp
// NVS.Core/Interfaces/
IWorkspaceService      // Workspace management
IProjectService        // Project loading, structure
IEditorService         // Text editing, documents
ILspClient            // Language server communication
IDebugService         // Debugging via DAP
IGitService           // Git operations
ITerminalService      // Terminal instances
IIndexService         // SQLite indexing, search
IBuildService         // Build execution
IPluginManager        // Plugin lifecycle
ILLMService           // AI assistance
ISettingsService      // Configuration
IThemeService         // Theming
IKeyBindingService    // Keybindings
```

## SQLite Schema (Per-Workspace)

```sql
-- .nvs/index.db

CREATE TABLE files (
    id INTEGER PRIMARY KEY,
    path TEXT UNIQUE NOT NULL,
    hash TEXT,
    last_indexed TEXT,
    language TEXT
);

CREATE TABLE symbols (
    id INTEGER PRIMARY KEY,
    file_id INTEGER REFERENCES files(id),
    name TEXT NOT NULL,
    kind TEXT,              -- class, method, function, etc.
    container TEXT,         -- parent symbol
    line_start INTEGER,
    line_end INTEGER,
    column_start INTEGER,
    column_end INTEGER,
    documentation TEXT
);

CREATE TABLE references (
    id INTEGER PRIMARY KEY,
    from_symbol_id INTEGER REFERENCES symbols(id),
    to_symbol_id INTEGER REFERENCES symbols(id),
    reference_kind TEXT     -- call, import, extends, etc.
);

CREATE VIRTUAL TABLE content_fts USING fts5(
    file_id,
    content,
    tokenize='porter unicode61'
);
```

## Configuration Files

### Global Settings (~/.nvs/settings.json)

```json
{
  "theme": "NVS Dark",
  "keybindingPreset": "vscode",
  "editor": {
    "fontSize": 14,
    "fontFamily": "JetBrains Mono",
    "tabSize": 4
  },
  "llm": {
    "endpoint": "http://localhost:11434/v1",
    "model": "codellama"
  }
}
```

### Workspace Settings (.nvs/workspace.json)

```json
{
  "folders": ["src", "tests"],
  "languageServers": {
    "csharp": { "command": "dotnet", "args": ["omnisharp"] },
    "cpp": { "command": "clangd" },
    "typescript": { "command": "typescript-language-server", "args": ["--stdio"] }
  },
  "debugConfigurations": [],
  "buildTasks": []
}
```

## Implementation Phases

### Phase 1: Foundation (Weeks 1-4)
- [x] Solution setup with .NET 10, AvaloniaUI
- [x] Core interfaces and DI setup
- [x] Basic shell UI (window, layout, menus)
- [x] Project/workspace model
- [x] Settings infrastructure

### Phase 2: Editor Core (Weeks 5-8) - IN PROGRESS
- [ ] AvaloniaEdit integration
- [x] Syntax highlighting (basic)
- [ ] File management (open/save/tabs)
- [ ] Basic commands (undo/redo/find)
- [x] File system watcher

### Phase 3: LSP Integration (Weeks 9-12)
- [ ] LSP client implementation (JSON-RPC)
- [ ] Language server management
- [ ] Auto-complete, go-to-definition, diagnostics
- [ ] Support: C#, C/C++ (ccls/clangd), TS/JS (typescript-language-server)

### Phase 4: Git + Terminal (Weeks 13-16)
- [ ] LibGit2Sharp integration
- [ ] Git status, diff view, commit UI
- [ ] Terminal emulator (Avalonia Xterm or custom PTY)
- [ ] PowerShell/Bash shell integration

### Phase 5: Debugging (DAP) (Weeks 17-21)
- [ ] DAP client implementation
- [ ] Breakpoints, call stack, variables view
- [ ] Debug adapters: netcoredbg, cpptools, node-debug
- [ ] Launch/attach configurations

### Phase 6: SQLite Indexing (Weeks 22-24)
- [ ] Project indexing schema
- [ ] Symbol search, file search
- [ ] Reference tracking

### Phase 7: Plugin System (Weeks 25-28)
- [ ] Plugin API design
- [ ] Dynamic loading (AssemblyLoadContext)
- [ ] Plugin manifest format
- [ ] Plugin marketplace (future)

### Phase 8: LLM/Agents (Phase 2)
- [ ] OpenAI-compatible API integration
- [ ] Code completion, chat interface
- [ ] Agent framework

## Key NuGet Packages

```xml
<!-- UI -->
<PackageVersion Include="Avalonia" Version="11.2.0" />
<PackageVersion Include="AvaloniaEdit" Version="11.1.0" />
<PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.0" />

<!-- Core -->
<PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
<PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="9.0.0" />
<PackageVersion Include="Serilog" Version="4.2.0" />
<PackageVersion Include="Serilog.Sinks.File" Version="6.0.0" />

<!-- Data -->
<PackageVersion Include="Dapper" Version="2.1.0" />
<PackageVersion Include="Microsoft.Data.Sqlite" Version="9.0.0" />

<!-- Git -->
<PackageVersion Include="LibGit2Sharp" Version="0.31.0" />

<!-- Testing -->
<PackageVersion Include="xunit" Version="2.9.0" />
<PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0" />
<PackageVersion Include="FluentAssertions" Version="7.0.0" />
<PackageVersion Include="NSubstitute" Version="5.3.0" />
```

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Config Format | JSON | Native .NET, simple, type-safe |
| Keybindings | Multiple presets | User chooses at first launch |
| Workspace Model | Hybrid | Folder-based + solution-based |
| Theming | Both JSON + VS Code | Maximum compatibility |
| Update Check | None | Package managers handle updates |
| Telemetry | None | Privacy-focused |
| Crash Reporting | Manual | User-controlled |
| LLM Integration | OpenAI-compatible | Supports llama.cpp, Ollama, OpenRouter, vLLM |
| Terminal | Evaluate both | xterm.js vs native PTY |
| Plugin API | Expose services | Full API access |
