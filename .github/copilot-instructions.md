# Copilot Instructions for NVS

NVS (No Vim Substitute) is a cross-platform IDE built with .NET 10 and AvaloniaUI 11.x.

## Build, Test, and Lint

```shell
# Build
dotnet build NVS.slnx

# Run all tests
dotnet test NVS.slnx

# Run a single test project
dotnet test tests/NVS.Services.Tests

# Run a single test by name
dotnet test NVS.slnx --filter "FullyQualifiedName~LanguageServiceTests.DetectLanguage"

# Run the app
dotnet run --project src/NVS
```

There is no separate lint command. Code style is enforced at build time via `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, and the .NET analyzers configured in `Directory.Build.props`.

## Architecture

Layered architecture with strict dependency direction:

```
NVS (UI)  →  NVS.Services / NVS.Infrastructure  →  NVS.Core
                                                      NVS.Plugins
```

- **NVS.Core** — Interfaces (`src/NVS.Core/Interfaces/`) and models only. No implementations. All service contracts live here.
- **NVS.Services** — Service implementations (EditorService, FileSystemService, WorkspaceService, LanguageService, SettingsService).
- **NVS.Infrastructure** — DI registration helpers, logging configuration (Serilog).
- **NVS.Plugins** — Plugin loading system using `AssemblyLoadContext`.
- **NVS** — Avalonia UI app: ViewModels, Views (AXAML), Behaviors, and the DI composition root in `App.axaml.cs`.

All services are registered as **singletons** in `App.axaml.cs :: ConfigureServices()`. ViewModels and Views are transient (except `EditorViewModel` which is singleton).

## Conventions

### C# / .NET

- Target: `net10.0` with `LangVersion preview` and nullable reference types enabled globally.
- Service implementations are `sealed` classes.
- Private fields use underscore prefix: `_editorService`.
- All service interface methods are async (`Task`-returning) with `CancellationToken` parameters.
- DTOs use `record` types with init-only properties.
- Services communicate via `EventHandler`-based events (e.g., `DocumentOpened`, `WorkspaceClosed`).

### MVVM

- ViewModels implement `INotifyPropertyChanged` manually with `SetProperty<T>()` — they do **not** use `[ObservableProperty]`.
- Commands use `[RelayCommand]` attribute from CommunityToolkit.Mvvm on `private async Task` methods.
- Compiled bindings are enabled (`AvaloniaUseCompiledBindingsByDefault`). AXAML files must declare `x:DataType`.

### Avalonia UI

- Dark theme only (FluentTheme with `RequestedThemeVariant="Dark"`).
- AvaloniaEdit requires its theme include: `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`.
- Text binding to AvaloniaEdit uses a custom `DocumentTextBindingBehavior` attached behavior (not a direct `Text` property).
- Syntax highlighting uses `TextEditorSyntaxHighlighting.Language` attached property with on-demand regex-based definitions.
- Multi-key chord keybindings are not supported in Avalonia — use single combinations (e.g., `Ctrl+Shift+O`).

### Testing

- Framework: xUnit + FluentAssertions + NSubstitute.
- Test projects have global usings for `FluentAssertions`, `NSubstitute`, and `Xunit`.
- Naming: `MethodName_Scenario_ExpectedOutcome` (e.g., `DetectLanguage_WithCSharpFile_ShouldReturnCSharp`).
- Use `[Theory]` with `[InlineData]` for parameterized tests; `[Fact]` for single cases.
- Assert with `.Should()` fluent API, never with `Assert.*`.
