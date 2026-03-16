# NVS Debug Adapter Implementation Guide for Java & PHP

## Quick Start: Adding a New Debug Adapter

### Phase 1: Minimal Implementation (30 minutes)

#### 1. Register the Adapter

**File:** src/NVS/App.axaml.cs (ConfigureServices method, around line 145)

`csharp
// After: services.AddSingleton<DebugAdapterRegistry>();

var registry = services.BuildServiceProvider().GetRequiredService<DebugAdapterRegistry>();

// Register Java adapter
registry.Register(new DebugAdapterInfo
{
    Type = "java",
    DisplayName = "Java (Eclipse Debug Server)",
    ExecutableName = "java-debug-server",
    Arguments = [],
    SupportedRuntimes = ["java", "jvm"],
});

// Register PHP adapter
registry.Register(new DebugAdapterInfo
{
    Type = "php",
    DisplayName = "PHP (VS Code PHP Debug)",
    ExecutableName = "php-debug",
    Arguments = [],
    SupportedRuntimes = ["php"],
});
`

**Result:** Adapters now discoverable, but require manual path configuration via DebugConfiguration.ServerPort or PATH.

---

### Phase 2: Auto-Download Support (1 hour)

#### 2. Extend DebugAdapterDownloader

**File:** src/NVS.Services/Debug/DebugAdapterDownloader.cs

Add new methods following netcoredbg pattern:

`csharp
// Java Debug Server
public async Task<string> EnsureJavaDebugAsync(
    IProgress<string>? progress = null,
    CancellationToken cancellationToken = default)
{
    var existing = GetInstalledPath("java-debug-server");
    if (existing is not null)
        return existing;

    // Download from Microsoft's Java Debug Server releases
    // https://github.com/microsoft/java-debug/releases
    const string version = "0.52.0";
    const string baseUrl = "https://github.com/microsoft/java-debug/releases/download";
    
    var url = $"{baseUrl}/v{version}/java-debug-server-{version}.tar.gz";
    var destDir = Path.Combine(_toolsDir, "java-debug-server");
    Directory.CreateDirectory(destDir);

    var tempFile = Path.Combine(Path.GetTempPath(), 
        \$"java-debug-{Guid.NewGuid()}.tar.gz");

    try
    {
        progress?.Report(\$"Downloading Java Debug Server {version}...\");
        await DownloadFileAsync(url, tempFile, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report("Extracting Java Debug Server...");
        await ExtractArchiveAsync(tempFile, destDir, ".tar.gz", cancellationToken)
            .ConfigureAwait(false);

        var result = GetInstalledPath("java-debug-server")
            ?? throw new InvalidOperationException(
                "java-debug-server not found after extraction.");

        progress?.Report("Java Debug Server installed successfully.");
        return result;
    }
    finally
    {
        try { File.Delete(tempFile); } catch { }
    }
}

// PHP Debug
public async Task<string> EnsurePhpDebugAsync(
    IProgress<string>? progress = null,
    CancellationToken cancellationToken = default)
{
    var existing = GetInstalledPath("php-debug");
    if (existing is not null)
        return existing;

    // Download from VS Code PHP Debug extension releases
    const string version = "1.32.1";
    const string baseUrl = "https://github.com/felixbecker/vscode-php-debug/releases/download";
    
    var url = \$"{baseUrl}/v{version}/php-debug-{version}.phar\";
    var destDir = Path.Combine(_toolsDir, "php-debug");
    Directory.CreateDirectory(destDir);

    var execPath = Path.Combine(destDir, "php-debug");
    
    progress?.Report(\$"Downloading PHP Debug {version}...\");
    using var response = await SharedHttpClient.GetAsync(url, 
        HttpCompletionOption.ResponseHeadersRead, cancellationToken)
        .ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
        .ConfigureAwait(false);
    await using var fileStream = File.Create(execPath);
    await stream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

    // Set executable permissions
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(execPath, UnixFileMode.UserRead | UnixFileMode.UserWrite 
            | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    progress?.Report("PHP Debug installed successfully.");
    return execPath;
}
`

#### 3. Extend ResolveAdapterPathAsync

**File:** src/NVS.Services/Debug/DebugService.cs (lines 57-88)

Update to handle new adapters:

`csharp
public async Task<string> ResolveAdapterPathAsync(string adapterType, 
    CancellationToken cancellationToken = default)
{
    var adapterInfo = _adapterRegistry.GetAdapter(adapterType)
        ?? throw new InvalidOperationException(
            \$"No debug adapter registered for type '{adapterType}'.\");

    var adapterPath = _adapterRegistry.FindAdapterExecutable(adapterType);

    // Auto-download if missing (currently only netcoredbg, extend here)
    if (adapterPath is null)
    {
        var progress = new Progress<string>(msg =>
            OutputReceived?.Invoke(this, new OutputEvent
            {
                Output = msg + "\\n",
                Category = OutputCategory.Console,
            }));

        adapterPath = adapterType switch
        {
            "coreclr" => await _adapterRegistry.Downloader
                .EnsureNetcoredbgAsync(progress, cancellationToken),
            "java" => await _adapterRegistry.Downloader
                .EnsureJavaDebugAsync(progress, cancellationToken),
            "php" => await _adapterRegistry.Downloader
                .EnsurePhpDebugAsync(progress, cancellationToken),
            _ => null,
        };
    }

    return adapterPath
        ?? throw new InvalidOperationException(
            \$"Debug adapter '{adapterInfo.DisplayName}' not found. " +
            \$"Please install {adapterInfo.ExecutableName} and ensure it's on your PATH.\");
}
`

**Result:** Java and PHP adapters auto-download on first debug session.

---

### Phase 3: Project Type Detection & Launch (2 hours)

#### 4. Detect Java/PHP Projects

**File:** src/NVS/ViewModels/MainViewModel.cs (new helper method, before StartDebugging)

`csharp
private (string Type, string Program) DetectProjectType(string projectPath)
{
    // Check for Java project indicators
    if (File.Exists(Path.Combine(projectPath, "pom.xml")))
        return ("java", DetectJavaMainClass(projectPath));
    
    if (File.Exists(Path.Combine(projectPath, "build.gradle")) ||
        File.Exists(Path.Combine(projectPath, "build.gradle.kts")))
        return ("java", DetectJavaMainClass(projectPath));

    // Check for PHP project indicators
    if (Directory.Exists(Path.Combine(projectPath, "vendor")) ||
        File.Exists(Path.Combine(projectPath, "composer.json")))
        return ("php", DetectPhpEntryPoint(projectPath));

    // Default to .NET/C#
    return ("coreclr", "");
}

private string DetectJavaMainClass(string projectPath)
{
    // Search for @SpringBootApplication or main() method in src/main/java
    var mainDir = Path.Combine(projectPath, "src", "main", "java");
    if (Directory.Exists(mainDir))
    {
        // Simplified: look for a file containing "public static void main"
        var javaFiles = Directory.GetFiles(mainDir, "*.java", 
            SearchOption.AllDirectories);
        foreach (var file in javaFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains("public static void main"))
            {
                // Extract package and class name
                var match = System.Text.RegularExpressions.Regex.Match(
                    content, @"package\\s+([\\w.]+);.*public\\s+class\\s+(\w+)");
                if (match.Success)
                    return \$"{match.Groups[1].Value}.{match.Groups[2].Value}\";
            }
        }
    }

    // Fallback: look for JAR or class files in target/
    var jarPath = Path.Combine(projectPath, "target", "*.jar");
    var jars = Directory.GetFiles(Path.GetDirectoryName(jarPath) ?? projectPath,
        Path.GetFileName(jarPath) ?? "*.jar", SearchOption.TopDirectoryOnly);
    if (jars.Length > 0)
        return jars[0];

    return "";
}

private string DetectPhpEntryPoint(string projectPath)
{
    // Check for index.php (web app)
    var indexPath = Path.Combine(projectPath, "index.php");
    if (File.Exists(indexPath))
        return indexPath;

    // Check for artisan or similar (Laravel, Symfony)
    var artisanPath = Path.Combine(projectPath, "artisan");
    if (File.Exists(artisanPath))
        return artisanPath;

    // Check composer.json for "bin" entry
    var composerPath = Path.Combine(projectPath, "composer.json");
    if (File.Exists(composerPath))
    {
        try
        {
            var json = JsonDocument.Parse(File.ReadAllText(composerPath));
            if (json.RootElement.TryGetProperty("bin", out var binElement))
            {
                var binArray = binElement.EnumerateArray().FirstOrDefault();
                if (binArray.ValueKind == JsonValueKind.String)
                    return Path.Combine(projectPath, binArray.GetString() ?? "");
            }
        }
        catch { }
    }

    return "";
}
`

#### 5. Update StartDebugging for Multi-Language Support

**File:** src/NVS/ViewModels/MainViewModel.cs (modify StartDebugging method)

Replace the hardcoded "coreclr" logic with:

`csharp
private async Task StartDebugging()
{
    if (_debugService is null)
    {
        StatusMessage = "Debug service not available";
        return;
    }

    if (_debugService.IsDebugging)
    {
        if (_debugService.IsPaused)
            await _debugService.ContinueAsync();
        return;
    }

    var solution = _solutionService.CurrentSolution;
    if (solution is null)
    {
        StatusMessage = "No solution loaded — cannot debug";
        return;
    }

    var startup = _solutionService.GetStartupProject();
    if (startup is null)
    {
        StatusMessage = "No startup project set — cannot debug";
        return;
    }

    try
    {
        _debugSessionGeneration++;
        StatusMessage = "Detecting project type...";
        
        var projectDir = Path.GetDirectoryName(startup.FilePath) ?? ".";
        var (adapterType, program) = DetectProjectType(projectDir);

        // Build before debug (language-specific)
        await BuildBeforeDebugAsync(adapterType, projectDir);

        // Create appropriate debug config based on project type
        var config = adapterType switch
        {
            "java" => new DebugConfiguration
            {
                Name = startup.Name,
                Type = "java",
                Request = "launch",
                Program = program,
                Cwd = projectDir,
                AdditionalProperties = new Dictionary<string, object>
                {
                    { "mainClass", program },
                    { "projectName", startup.Name },
                    { "cwd", projectDir },
                },
            },
            "php" => new DebugConfiguration
            {
                Name = startup.Name,
                Type = "php",
                Request = "launch",
                Program = program,
                Cwd = projectDir,
                AdditionalProperties = new Dictionary<string, object>
                {
                    { "runtimeExecutable", "php" },
                    { "port", 9003 },
                },
            },
            _ => CreateNetCoreDebugConfig(startup, projectDir), // Default .NET
        };

        StatusMessage = "Starting debugger...";
        await _debugService.StartDebuggingAsync(config);
    }
    catch (Exception ex)
    {
        StatusMessage = \$"Debug error: {ex.Message}\";
    }
}

private async Task BuildBeforeDebugAsync(string adapterType, string projectDir)
{
    StatusMessage = "Building before debug...";
    var buildOutput = FindBuildOutputTool();
    buildOutput?.ClearOutput();

    switch (adapterType)
    {
        case "java":
            // Maven: mvn clean package -DskipTests
            // Gradle: gradle clean build -x test
            if (File.Exists(Path.Combine(projectDir, "pom.xml")))
                await RunBuildTask("Maven Build", "mvn", 
                    ["clean", "package", "-DskipTests"], projectDir);
            else if (File.Exists(Path.Combine(projectDir, "build.gradle")))
                await RunBuildTask("Gradle Build", "gradle",
                    ["clean", "build", "-x", "test"], projectDir);
            break;

        case "php":
            // PHP: composer install (if composer.json exists)
            if (File.Exists(Path.Combine(projectDir, "composer.json")))
                await RunBuildTask("Composer Install", "composer",
                    ["install", "--no-interaction"], projectDir);
            break;

        default: // .NET
            var buildTask = new Core.Interfaces.BuildTask
            {
                Name = "Build for Debug",
                Command = "dotnet",
                Args = ["build", _solutionService.CurrentSolution?.FilePath ?? ""],
                WorkingDirectory = projectDir,
            };
            await RunBuildTaskAsync(buildTask);
            break;
    }
}

private async Task RunBuildTask(string taskName, string command, 
    IReadOnlyList<string> args, string workDir)
{
    var buildTask = new Core.Interfaces.BuildTask
    {
        Name = taskName,
        Command = command,
        Args = [.. args],
        WorkingDirectory = workDir,
    };
    await RunBuildTaskAsync(buildTask);
}

private async Task RunBuildTaskAsync(Core.Interfaces.BuildTask buildTask)
{
    var buildOutput = FindBuildOutputTool();
    void OnBuildOutput(object? sender, Core.Interfaces.BuildOutputEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            buildOutput?.AppendOutput(e.Output, e.IsError));
    }

    _buildService.OutputReceived += OnBuildOutput;
    try
    {
        var result = await _buildService.RunTaskAsync(buildTask);
        if (!result.Success)
        {
            StatusMessage = \$"Build failed — {result.Errors.Count} error(s)\";
            FindProblemsTool()?.SetProblems(result.Errors, result.Warnings);
            throw new InvalidOperationException("Build failed");
        }
    }
    finally
    {
        _buildService.OutputReceived -= OnBuildOutput;
    }
}

private DebugConfiguration CreateNetCoreDebugConfig(
    Core.Interfaces.ProjectInfo startup, string projectDir)
{
    var assemblyName = startup.AssemblyName ?? startup.Name;
    var programPath = Path.Combine(projectDir, "bin", "Debug", 
        startup.TargetFramework, assemblyName + ".dll");

    if (!File.Exists(programPath))
    {
        var binDir = Path.Combine(projectDir, "bin", "Debug");
        if (Directory.Exists(binDir))
        {
            var found = Directory.GetFiles(binDir, assemblyName + ".dll",
                SearchOption.AllDirectories);
            if (found.Length > 0)
                programPath = found[0];
        }
    }

    var isConsoleApp = string.Equals(startup.OutputType, "Exe", 
        StringComparison.OrdinalIgnoreCase) || startup.OutputType is null;

    if (isConsoleApp)
    {
        // Console app: attach mode with hook
        _debugUsesTerminal = true;
        var hookDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
            "tools", "DebugStartupHook.dll");
        if (!File.Exists(hookDll))
            throw new FileNotFoundException("Debug startup hook not found.", hookDll);

        var pidFile = Path.Combine(Path.GetTempPath(), 
            \$"nvs_debug_{Guid.NewGuid():N}.pid\");
        var readyFile = pidFile + ".ready";
        var goFile = pidFile + ".go";

        // (rest of console app logic as before)
        // ...

        return new DebugConfiguration
        {
            Name = startup.Name,
            Type = "coreclr",
            Request = "attach",
            ProcessId = 0, // Will be set after hook writes PID file
            Cwd = projectDir,
        };
    }
    else
    {
        // GUI app: launch mode
        _debugUsesTerminal = false;
        return new DebugConfiguration
        {
            Name = startup.Name,
            Type = "coreclr",
            Request = "launch",
            Program = programPath,
            Cwd = projectDir,
        };
    }
}
`

---

### Phase 4: Test & Validate

#### 6. Test Configuration

**File:** 	ests/NVS.Services.Tests/DebugAdapterTests.cs (new test class)

`csharp
[TestClass]
public class MultiAdapterDebugTests
{
    [TestMethod]
    public async Task JavaAdapter_RegistersSuccessfully()
    {
        var registry = new DebugAdapterRegistry();
        registry.Register(new DebugAdapterInfo
        {
            Type = "java",
            DisplayName = "Java Debug",
            ExecutableName = "java-debug-server",
            Arguments = [],
            SupportedRuntimes = ["java"],
        });

        var adapter = registry.GetAdapter("java");
        Assert.IsNotNull(adapter);
        Assert.AreEqual("java", adapter.Type);
    }

    [TestMethod]
    public async Task PhpAdapter_RegistersSuccessfully()
    {
        var registry = new DebugAdapterRegistry();
        registry.Register(new DebugAdapterInfo
        {
            Type = "php",
            DisplayName = "PHP Debug",
            ExecutableName = "php-debug",
            Arguments = [],
            SupportedRuntimes = ["php"],
        });

        var adapter = registry.GetAdapter("php");
        Assert.IsNotNull(adapter);
        Assert.AreEqual("php", adapter.Type);
    }

    [TestMethod]
    public void ProjectTypeDetection_ReturnsJavaForMavenProject()
    {
        // Create temp directory with pom.xml
        var tempDir = Path.Combine(Path.GetTempPath(), "test-java-project");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "pom.xml"), "<project></project>");

        var viewModel = new MainViewModel(/* dependencies */);
        var (type, program) = viewModel.DetectProjectType(tempDir);

        Assert.AreEqual("java", type);
        Directory.Delete(tempDir, true);
    }

    [TestMethod]
    public void ProjectTypeDetection_ReturnsPhpForComposerProject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test-php-project");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "composer.json"), "{}");

        var viewModel = new MainViewModel(/* dependencies */);
        var (type, program) = viewModel.DetectProjectType(tempDir);

        Assert.AreEqual("php", type);
        Directory.Delete(tempDir, true);
    }
}
`

---

## Architecture Diagram

`
┌─────────────────────────────────────────────────────────────────────────┐
│                            NVS IDE (Avalonia)                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  MainViewModel                                                            │
│  ├─ StartDebugging()     ◄────────────────────────────────┐             │
│  │   ├─ Detect Project Type (Java/PHP/.NET)               │             │
│  │   ├─ Build Project (Maven/Gradle/Composer/dotnet)      │             │
│  │   └─ DebugService.StartDebuggingAsync(config)           │             │
│  │                                                          │             │
│  ├─ VariablesToolViewModel                                 │             │
│  │   └─ GetVariablesAsync()                                │             │
│  │                                                          │             │
│  └─ Editor Gutter                                           │             │
│      └─ BreakpointStore.ToggleBreakpoint()                 │             │
│                                                              │             │
└──────────────────────────────────┬──────────────────────────┼─────────────┘
                                   │                          │
                   ┌───────────────┘                          │
                   │ IDebugService                            │
                   ▼                                          │
┌────────────────────────────────────────────────────────────┼─────────────┐
│                    DebugService (Singleton)                │             │
├────────────────────────────────────────────────────────────┼─────────────┤
│                                                             │             │
│  ResolveAdapterPathAsync(type)  ◄──────────────────────────┘             │
│  ├─ DebugAdapterRegistry.FindAdapterExecutable()                         │
│  └─ DebugAdapterDownloader.EnsureXxxAsync() [auto-download]             │
│                                                                           │
│  StartDebuggingAsync(config)                                             │
│  ├─ Launch adapter process (stdio) OR Connect via TCP                    │
│  │                                                                       │
│  ├─ Create DapClient                                                     │
│  │   ├─ DapClient.InitializeAsync()   ──┐                              │
│  │   ├─ DapClient.LaunchAsync/AttachAsync() ──┼─────► Debug Adapter     │
│  │   ├─ Wait for "initialized" event   ──┤  (netcoredbg/java-debug/    │
│  │   ├─ SyncBreakpoints()               ──┤   php-debug)              │
│  │   └─ ConfigurationDoneAsync()        ──┘                             │
│  │                                                                       │
│  └─ Event Handlers                                                       │
│      ├─ OnClientStopped() → DebuggingPaused event                       │
│      ├─ OnClientTerminated() → CleanupSession()                         │
│      ├─ OnClientOutput() → OutputReceived event                         │
│      └─ OnClientThreadEvent() → ThreadStarted/Exited events            │
│                                                                          │
└──────────────────────┬──────────────────────────────────────────────────┘
                       │
        ┌──────────────┼──────────────┬──────────────┐
        │              │              │              │
        ▼              ▼              ▼              ▼
   DapClient    DebugAdapter    BreakpointStore    DebugAdapterRegistry
   (DAP Client) Registry        (In-Memory)        & Downloader
   
   ├─ SendRequest  ├─ Type: "coreclr" ├─ Dict<Path,   ├─ Registry:
   ├─ Listen       │  - Display        │   List<BP>>   │  {Type: Info}
   ├─ Events       │                   │               │
   │               │ Type: "java"      ├─ Toggle       ├─ Downloader:
   │ Std Input/    │  - Display        │ GetAll        │  GetInstalled()
   │ Output        │  - ExecutableName │ Clear         │  EnsureXxx()
   │               │  - Arguments      │ UpdateStatus  │
   │               │                   │               │
   │               │ Type: "php"       │ Events:       │
   │               │  - Display        │ BreakpointCh  │
   │               │  - ExecutableName │ anged         │
   │               │  - Arguments      │               │
   │               │                   │               │
   └───────────────┴───────────────────┴───────────────┴──────────────────

    ┌────────────────────────────────────────────────────────────────────┐
    │                      DAP Protocol Messages                          │
    ├────────────────────────────────────────────────────────────────────┤
    │                                                                    │
    │  Request        Response         Event                             │
    │  ────────       ────────         ─────                             │
    │  initialize     capabilities     initialized                       │
    │  launch         success          stopped                           │
    │  attach         success          terminated                        │
    │  setBreakpoints breakpoints      output                            │
    │  threads        threads          thread                            │
    │  stackTrace     stackFrames      breakpoint                        │
    │  scopes         scopes                                             │
    │  variables      variables                                          │
    │  evaluate       result                                             │
    │  continue       success                                            │
    │  next           success                                            │
    │  stepIn         success                                            │
    │  stepOut        success                                            │
    │  pause          success                                            │
    │  disconnect     success                                            │
    │                                                                    │
    └────────────────────────────────────────────────────────────────────┘
`

---

## Checklist for Adding Java Support

- [ ] Register Java adapter in App.axaml.cs
- [ ] Add EnsureJavaDebugAsync() to DebugAdapterDownloader
- [ ] Update ResolveAdapterPathAsync() to call EnsureJavaDebugAsync() for "java" type
- [ ] Add Java detection in DetectProjectType() (pom.xml / build.gradle)
- [ ] Implement DetectJavaMainClass() to find entry point
- [ ] Update BuildBeforeDebugAsync() to handle Maven/Gradle
- [ ] Update StartDebugging() to create Java DebugConfiguration
- [ ] Test: Create sample Maven/Gradle project, set breakpoint, debug
- [ ] Test: Verify auto-download of java-debug-server

## Checklist for Adding PHP Support

- [ ] Register PHP adapter in App.axaml.cs
- [ ] Add EnsurePhpDebugAsync() to DebugAdapterDownloader
- [ ] Update ResolveAdapterPathAsync() to call EnsurePhpDebugAsync() for "php" type
- [ ] Add PHP detection in DetectProjectType() (composer.json / index.php)
- [ ] Implement DetectPhpEntryPoint() to find entry file
- [ ] Update BuildBeforeDebugAsync() to handle Composer (if needed)
- [ ] Update StartDebugging() to create PHP DebugConfiguration
- [ ] Configure Xdebug port (9003 standard)
- [ ] Test: Create sample PHP project, set breakpoint, debug
- [ ] Test: Verify auto-download of php-debug

---

## Common Pitfalls

1. **Adapter Not Found:** Missing PATH setup or wrong ExecutableName
   - Fix: Ensure adapter binary is on PATH or implement auto-download

2. **Wrong Program Path:** Incorrect build output directory
   - Fix: Verify build system output location (target/ for Java, build/ for some projects)

3. **Port Conflicts:** PHP Xdebug default port (9003) already in use
   - Fix: Allow config override for port, use dynamic port selection

4. **Timeout on Initialization:** Adapter takes long to start
   - Fix: Increase timeout in StartDebuggingAsync (currently 10 seconds)
   - Consider showing progress dialog to user

5. **Breakpoint Not Hit:** Breakpoints set before debuggee loaded
   - Fix: Re-sync breakpoints after module load (already done for .NET)

---

## References

- **Eclipse Java Debug Server:** https://github.com/microsoft/java-debug
- **VS Code PHP Debug:** https://github.com/felixbecker/vscode-php-debug
- **Debug Adapter Protocol Spec:** https://microsoft.github.io/debug-adapter-protocol/specification
- **netcoredbg GitHub:** https://github.com/Samsung/netcoredbg

