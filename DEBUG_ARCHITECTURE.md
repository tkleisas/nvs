# NVS Debug Adapter Infrastructure Architecture

## Overview
The NVS project implements a complete Debug Adapter Protocol (DAP) infrastructure enabling multi-language debugging support. Currently configured for C#/.NET (netcoredbg), the architecture is designed for extensibility to support Java, PHP, and other languages.

## 1. Architecture Layers

### Core Layer (NVS.Core)
**Location:** src/NVS.Core/

#### Interfaces
- **IDebugService** (Interfaces/IDebugService.cs)
  - Primary service interface for debug operations
  - Manages debug session lifecycle
  - Coordinates with debug adapters via DAP protocol
  - Key methods:
    - StartDebuggingAsync(DebugConfiguration) - Launches debug session
    - StopDebuggingAsync() - Terminates session
    - PauseAsync(), ContinueAsync() - Execution control
    - StepOverAsync(), StepIntoAsync(), StepOutAsync() - Step operations
    - SetBreakpointsAsync() - Breakpoint management
    - GetVariablesAsync(), EvaluateAsync() - Variable inspection
    - ResolveAdapterPathAsync() - Auto-downloads adapters if needed
  - Properties: IsDebugging, IsPaused, CurrentSession, ActiveThreadId, ActiveFrameId
  - Events: DebuggingStarted, DebuggingStopped, DebuggingPaused, DebuggingContinued, ThreadStarted, ThreadExited, BreakpointHit, OutputReceived

- **IDapClient** (Interfaces/IDapClient.cs)
  - Low-level DAP client interface
  - Manages request/response protocol with adapter
  - Handles asynchronous DAP messages
  - Key methods: InitializeAsync(), LaunchAsync(), AttachAsync(), DisconnectAsync(), SetBreakpointsAsync(), etc.
  - Events: Stopped, Terminated, OutputReceived, ThreadEvent, BreakpointEvent, Initialized

- **IBreakpointStore** (Interfaces/IBreakpointStore.cs)
  - In-memory breakpoint management
  - Thread-safe storage per-file
  - Methods: ToggleBreakpoint(), GetBreakpoints(), GetAllBreakpoints(), UpdateVerifiedStatus()
  - Event: BreakpointChanged

#### Models
- **DebugConfiguration** (Models/Settings/DebugConfiguration.cs)
  - Configuration for starting debug session
  - Properties:
    - Name - Display name (e.g., "MyApp Debug")
    - Type - Adapter type (e.g., "coreclr" for C#, future: "java", "php")
    - Request - "launch" or "attach"
    - Program - Path to executable/assembly
    - Cwd - Working directory
    - Args - Command-line arguments
    - Console - Console mode ("integratedTerminal", "internalConsole", "externalTerminal")
    - ServerPort - Optional: Connect to existing adapter on TCP port
    - ProcessId - Optional: For attach mode, target process ID
    - AdditionalProperties - Language-specific settings (extensible dictionary)

- **Related Records** (in IDebugService.cs)
  - DebugSession - Active session metadata (Id, Name, Type, StartedAt)
  - ThreadInfo - Thread data (Id, Name)
  - StackFrame - Stack frame (Id, Name, Source, Line, Column)
  - Variable - Variable for inspection (Name, Value, Type, VariablesReference)
  - Breakpoint - Breakpoint data (Id, Path, Line, IsEnabled, IsVerified, Condition, HitCount)
  - OutputEvent - Debug output (Output, Category)
  - RunInTerminalRequest - Reverse request from adapter (Cwd, Args, Env, Title)

#### DAP Protocol Types
- **Location:** src/NVS.Core/Debug/DapProtocolTypes.cs
- Complete set of DAP protocol message types with JSON serialization
- Categories:
  - Initialize: DapInitializeRequestArguments, DapCapabilities
  - Launch/Attach: DapLaunchRequestArguments, DapAttachRequestArguments
  - Breakpoints: DapSetBreakpointsArguments, DapBreakpoint, DapSourceBreakpoint
  - Stack Trace: DapStackTraceArguments, DapStackFrame
  - Variables: DapVariablesArguments, DapVariable, DapScope, DapScopesResponseBody
  - Threads: DapThreadsResponseBody, DapThread
  - Execution: DapContinueArguments, DapNextArguments, DapStepInArguments, DapStepOutArguments, DapPauseArguments
  - Events: DapStoppedEventBody, DapTerminatedEventBody, DapOutputEventBody, DapThreadEventBody, DapBreakpointEventBody
  - RunInTerminal: DapRunInTerminalArguments, DapRunInTerminalResponseBody
  - Evaluate: DapEvaluateArguments, DapEvaluateResponseBody

#### Message Framing
- **Location:** src/NVS.Core/Debug/DapMessage.cs
- Base message types for DAP protocol
- DapMessage - Base class
- DapRequest - Request messages (Seq, Command, Arguments)
- DapResponse - Response messages (RequestSeq, Success, Message, Body)
- DapEvent - Event messages (Event, Body)
- Custom JSON converter: DapMessageConverter for polymorphic deserialization

---

### Services Layer (NVS.Services)
**Location:** src/NVS.Services/Debug/

#### DebugService (Implementation)
**File:** DebugService.cs (606 lines)

Core implementation of IDebugService.

**Key Responsibilities:**
1. **Session Management**
   - Maintains current debug session state
   - Handles session lifecycle (startup, cleanup, teardown)
   - Prevents concurrent debugging sessions
   - Tracks prior sessions to avoid state leaks

2. **Adapter Lifecycle**
   - Launches debug adapter subprocess (stdio-based)
   - OR connects to remote adapter via TCP (ServerPort config)
   - Manages adapter process lifecycle (spawn, kill, cleanup)
   - Resolves adapter paths (PATH search, common locations, auto-download)

3. **DAP Protocol Orchestration**
   - Initialize → Launch/Attach → Initialized event → SetBreakpoints → ConfigurationDone
   - Handles reverse requests (e.g., "runInTerminal")
   - Manages event subscription/unsubscription
   - Bridges DAP types to application-level abstractions

4. **Breakpoint Management**
   - Syncs breakpoints from store to adapter
   - Updates verified status after adapter response
   - Supports breakpoint re-syncing (e.g., after module load)

5. **Variable & Expression Evaluation**
   - Retrieves scopes for frames
   - Queries variables and child variables
   - Evaluates expressions at breakpoints

6. **Execution Control**
   - Continue, Pause, Step Over, Step In, Step Out
   - Thread management (get threads, set active thread)
   - Stack trace retrieval

7. **State Tracking**
   - IsDebugging, IsPaused, CurrentSession
   - ActiveThreadId, ActiveFrameId for context
   - Maintains internal _client (DapClient), _adapterProcess (Process), _tcpClient (TcpClient)

**Event Handlers:**
- Subscribes to DapClient events: Stopped, Terminated, OutputReceived, ThreadEvent
- Transforms DAP events to application events
- Emits: DebuggingStarted, DebuggingStopped, DebuggingPaused, DebuggingContinued, ThreadStarted, ThreadExited, BreakpointHit, OutputReceived

**Startup Flow (Console App Example):**
`
1. User clicks "Debug"
2. MainViewModel.StartDebugging() called
3. Build project to generate DLL
4. Launch hook-wrapped debuggee in terminal (attach mode)
5. DebugService.StartDebuggingAsync(config with ProcessId) called
6. Resolve netcoredbg path (auto-download if missing)
7. Launch netcoredbg process
8. DapClient.Initialize() → adapter responds with capabilities
9. DapClient.Attach(ProcessId) → attach to running process
10. Wait for "initialized" event
11. DapClient.SetBreakpoints() → sync all breakpoints
12. DapClient.ConfigurationDone() → adapter starts execution
13. Breakpoint signal files written (ready, go) → hook unpauses debuggee
14. Debuggee continues, adapter reports stopped events
`

**Cleanup:**
- StopDebuggingAsync() → DisconnectAsync(terminateDebuggee: true) → CleanupSessionAsync()
- Kills adapter process with entire tree
- Disposes TCP client if used
- Unsubscribes from events
- Sets IsDebugging = false

---

#### DapClient
**File:** DapClient.cs (380 lines)

Low-level DAP protocol client.

**Responsibilities:**
1. **Message I/O**
   - Reads/writes DapMessages via DapTransport
   - Manages request sequence numbers
   - Correlates responses to requests using ConcurrentDictionary<int, TaskCompletionSource>

2. **Listener Thread**
   - Background task reads messages from adapter continuously
   - Routes responses to pending requests
   - Dispatches events to subscribers

3. **Request/Response Pattern**
   - SendRequestAsync() - Enqueues request, waits for matching response
   - Throws DapRequestException if response indicates failure

4. **Reverse Requests**
   - Handles "runInTerminal" reverse requests from adapter
   - Responds with ProcessId from RunInTerminalHandler delegate
   - Falls back to empty response for unknown reverse requests

5. **Connection Management**
   - IsConnected property tracks initialization state
   - StartListening() spawns background listener
   - DisposeAsync() cancels listener, disconnects gracefully with timeout

**Event Subscription Model:**
- No events are auto-wired; DebugService subscribes explicitly
- Events marshaled via deserialization in listener thread
- Example: "stopped" event → deserialize to DapStoppedEventBody → invoke Stopped event

---

#### DapTransport
**File:** DapTransport.cs (~130 lines)

Transport layer for Content-Length framed messages (identical to LSP).

**Protocol:**
`
Content-Length: {byte_count}\r\n
\r\n
{json_body}
`

**Responsibilities:**
1. **Reading Messages**
   - Parse Content-Length header
   - Read exact number of bytes
   - Deserialize JSON to DapMessage (polymorphic via converter)

2. **Writing Messages**
   - Serialize message to JSON
   - Write Content-Length header
   - Write JSON body
   - Flush stream

3. **Thread Safety**
   - Read lock and write lock prevent interleaving
   - Semaphore-based mutual exclusion

4. **Error Handling**
   - Returns null on EOF (adapter exited)
   - Throws on I/O errors
   - Best-effort stream closure

---

#### DebugAdapterRegistry
**File:** DebugAdapterRegistry.cs (108 lines)

Central registry for debug adapter metadata and configuration.

**Current Built-in Adapters:**
`csharp
Register(new DebugAdapterInfo
{
    Type = "coreclr",
    DisplayName = ".NET (netcoredbg)",
    ExecutableName = "netcoredbg",
    Arguments = ["--interpreter=vscode"],
    SupportedRuntimes = ["dotnet"],
});
`

**Key Methods:**
1. Register(DebugAdapterInfo) - Add or update adapter
2. GetAdapter(type) - Retrieve by type key
3. GetAllAdapters() - List all registered adapters
4. FindAdapterExecutable(type) - Locate executable:
   - PATH environment variable
   - Common system locations (Windows: LocalAppData, ProgramFiles; Unix: /usr/local/bin, /usr/bin, ~/.dotnet/tools, ~/.local/bin)
   - NVS tools directory (auto-downloaded adapters)

**Extension Point for New Adapters:**
- Call egistry.Register() at app startup (in App.axaml.cs ConfigureServices)
- Define new DebugAdapterInfo with Type, DisplayName, ExecutableName, Arguments
- Example for Java:
`csharp
Register(new DebugAdapterInfo
{
    Type = "java",
    DisplayName = "Java (java-debug-server)",
    ExecutableName = "java-debug-server",
    Arguments = ["-agentlib:jdwp=transport=dt_socket,server=y,suspend=n"],
    SupportedRuntimes = ["java"],
});
`

---

#### DebugAdapterDownloader
**File:** DebugAdapterDownloader.cs (225 lines)

Auto-download and install debug adapters on demand.

**Current Support:**
- **netcoredbg v3.1.3-1062** via GitHub releases
- Detects OS and architecture, downloads appropriate binary
- Windows: .zip format
- macOS: .tar.gz (amd64 only)
- Linux: .tar.gz (amd64 or arm64 based on architecture)

**Installation Location:**
- Default: ~/.nvs/tools/{adapter_name}/
- Customizable via constructor parameter

**Key Methods:**
1. GetInstalledPath(adapterName) - Check if already installed locally
2. EnsureNetcoredbgAsync() - Download if missing, return executable path
   - Reports progress via IProgress<string>
   - Extracts archive
   - Sets Unix executable permissions
   - Returns full path to executable

**Download Flow:**
`
Check if installed → If missing, download from GitHub → Extract → Set permissions → Return path
`

**Extension Point for New Adapters:**
- Add new static method (e.g., EnsureJavaDebugAsync())
- Define download URLs, archive format, extraction logic
- Follow same pattern: check local, download, extract, return path

---

#### BreakpointStore
**File:** BreakpointStore.cs (157 lines)

In-memory breakpoint management, thread-safe, per-file tracking.

**Data Structure:**
`csharp
Dictionary<string, List<Breakpoint>> _breakpoints
// Key: file path (case-insensitive on Windows)
// Value: list of breakpoints in that file
`

**Key Methods:**
1. ToggleBreakpoint(path, line) - Add if missing, remove if exists
2. GetBreakpoints(path) - List all breakpoints in file
3. GetAllBreakpoints() - Flatten all breakpoints across files
4. RemoveBreakpoint(path, line) - Explicitly remove
5. ClearBreakpoints(path) - Remove all in file
6. ClearAllBreakpoints() - Remove everything
7. UpdateVerifiedStatus(path, line, verified) - Update after adapter response

**Thread Safety:**
- Lock-based synchronization on _lock object
- All public methods lock before accessing dictionary

**Events:**
- BreakpointChanged event for each mutation
- Emitted with BreakpointChangedEventArgs (FilePath, Breakpoint, Kind: Added/Removed/Updated)
- Allows UI to react to breakpoint changes in real-time

---

## 2. Dependency Injection Setup

**Location:** src/NVS/App.axaml.cs (ConfigureServices method, lines 122-158)

`csharp
services.AddSingleton<IDebugService, DebugService>();
services.AddSingleton<IBreakpointStore, BreakpointStore>();
services.AddSingleton<DebugAdapterDownloader>();
services.AddSingleton<DebugAdapterRegistry>();
`

**Lifetime:** Singleton (one instance per application lifetime)

**Construction Chain:**
- DebugService constructor takes DebugAdapterRegistry and optional IBreakpointStore
- DebugAdapterRegistry constructor creates default DebugAdapterDownloader
- Both registry and downloader are singletons, shared across service

---

## 3. Debug Session Flow (Detailed)

### Starting Debug (from UI)
1. **MainViewModel.StartDebugging()** (src/NVS/ViewModels/MainViewModel.cs:860)
   - Validates debug service available, no active session
   - Gets current solution and startup project
   - Builds solution (dotnet build)
   - Locates DLL in bin/Debug/{TargetFramework}

2. **For Console Apps (Interactive Mode):**
   - Creates debug terminal
   - Writes debug signal files (pidFile, readyFile, goFile)
   - Launches hook-wrapped executable in terminal
   - Waits for PID file (startup hook writes debuggee PID)
   - Creates DebugConfiguration with Request: "attach", ProcessId: {pid}
   - Calls DebugService.StartDebuggingAsync(config)

3. **For GUI Apps (Direct Launch):**
   - Creates DebugConfiguration with Request: "launch", Program: {dllPath}
   - Calls DebugService.StartDebuggingAsync(config)

4. **DebugService.StartDebuggingAsync()**
   - Acquires session lock (serializes debug starts)
   - Checks not already debugging
   - Cleans up prior session state (dispose old client, kill old process)
   - Calls ResolveAdapterPathAsync() to find/download netcoredbg
   - Creates adapter process (or connects via TCP if ServerPort set)
   - Creates DapClient with stdio transport
   - Subscribes to client events
   - **DAP Protocol Sequence:**
     `
     1. DapClient.InitializeAsync() 
        → adapter: initialize response (capabilities)
     2. DapClient.Attach(ProcessId) OR DapClient.Launch(program, args, cwd)
        → adapter: launch/attach response
     3. Wait for adapter "initialized" event (up to 10 seconds)
     4. DebugService.SyncBreakpointsToAdapterAsync()
        → foreach breakpoint file: SetBreakpoints request
     5. DapClient.ConfigurationDoneAsync()
        → adapter starts execution
     `
   - Emits DebuggingStarted event
   - Returns DebugSession with session Id, Name, Type

5. **For Console Apps (Post-Start):**
   - Writes readyFile (signals hook: ConfigurationDone complete)
   - Delays 500ms (wait for module load)
   - Calls DebugService.ResyncBreakpointsAsync() (re-send breakpoints)
   - Writes goFile (signals hook: resume execution)

### During Debug Session
- **User sets breakpoint:** Editor calls BreakpointStore.ToggleBreakpoint() → UI updates immediately
- **Debuggee hits breakpoint:** Adapter sends "stopped" event → DapClient.Stopped event → DebugService.OnClientStopped() → UI freezes on breakpoint
- **User inspects variables:** MainViewModel calls DebugService.GetVariablesAsync(frameId) → returns Variable list
- **User steps:** MainViewModel calls DebugService.StepOverAsync() (or StepIntoAsync/StepOutAsync) → DapClient sends request → execution resumes

### Stopping Debug
1. **MainViewModel.StopDebugging()**
2. **DebugService.StopDebuggingAsync()**
   - Attempts graceful disconnect (3-second timeout)
   - Calls CleanupSessionAsync()
3. **CleanupSessionAsync()**
   - Sets IsDebugging = false, IsPaused = false, CurrentSession = null
   - Unsubscribes from client events
   - Disposes DapClient (cancels listener, closes transport)
   - Kills adapter process with entire tree
   - Disposes TCP client if used
   - Emits DebuggingStopped event
4. **MainViewModel Cleanup**
   - Clears UI state (current line, variables panel)
   - Destroys debug terminal if used
   - Cleans up debuggee process if needed

---

## 4. Adapters Configuration

### Current: .NET/C# via netcoredbg

**Type:** "coreclr"
**Display Name:** ".NET (netcoredbg)"
**Executable:** netcoredbg (auto-downloadable)
**Arguments:** --interpreter=vscode
**Supported Runtimes:** ["dotnet"]

**Configuration Example:**
`csharp
new DebugConfiguration
{
    Name = "MyApp Debug",
    Type = "coreclr",
    Request = "launch",  // or "attach"
    Program = "/path/to/bin/Debug/net8.0/MyApp.dll",
    Cwd = "/path/to/project",
    Args = ["--arg1", "value1"],
    Console = "integratedTerminal",
}
`

---

## 5. Extension Points for Java & PHP

### Adding Java Debug Support

**Step 1: Register Adapter in App.axaml.cs**
`csharp
var javaAdapter = new DebugAdapterInfo
{
    Type = "java",
    DisplayName = "Java (JDWP)",
    ExecutableName = "java-debug-server",  // or wrapper script
    Arguments = ["-agentlib:jdwp=transport=dt_socket,server=y,suspend=n"],
    SupportedRuntimes = ["java", "jvm"],
};
adapterRegistry.Register(javaAdapter);
`

**Step 2: Extend DebugAdapterDownloader (if needed)**
`csharp
public async Task<string> EnsureJavaDebugAsync(IProgress<string>? progress = null)
{
    var existing = GetInstalledPath("java-debug-server");
    if (existing is not null) return existing;
    
    // Download from GitHub or Maven Central
    var url = "...";
    var destDir = Path.Combine(_toolsDir, "java-debug-server");
    // Extract and return path
}
`

**Step 3: Extend DebugConfiguration Model (optional)**
`csharp
// For Java-specific settings in AdditionalProperties:
new DebugConfiguration
{
    Name = "MyJavaApp",
    Type = "java",
    Request = "launch",
    Program = "/path/to/App.jar",
    AdditionalProperties = new Dictionary<string, object>
    {
        { "mainClass", "com.example.Main" },
        { "projectName", "my-app" },
        { "javaExe", "/usr/bin/java" },
    }
}
`

**Step 4: Update MainViewModel (if custom launch logic needed)**
- Detect Java projects (e.g., via presence of pom.xml, build.gradle, or .java files)
- Build appropriately (mvn compile, gradle build, etc.)
- Construct DebugConfiguration with Type: "java"
- Call DebugService.StartDebuggingAsync(config)

### Adding PHP Debug Support

**Step 1: Register Adapter in App.axaml.cs**
`csharp
var phpAdapter = new DebugAdapterInfo
{
    Type = "php",
    DisplayName = "PHP (Xdebug/Zend Debugger)",
    ExecutableName = "vscode-php-debug",  // or similar
    Arguments = [],
    SupportedRuntimes = ["php", "php-cli"],
};
adapterRegistry.Register(phpAdapter);
`

**Step 2: Extend DebugConfiguration (for PHP-specific settings)**
`csharp
new DebugConfiguration
{
    Name = "MyPHPApp",
    Type = "php",
    Request = "launch",
    Program = "/path/to/index.php",
    Cwd = "/path/to/web/root",
    AdditionalProperties = new Dictionary<string, object>
    {
        { "runtimeExecutable", "/usr/bin/php" },
        { "serverReadyAction", new { ... } },
        { "pathMapping", new { ... } },
        { "xdebugSettings", new { ... } },
    }
}
`

**Step 3: Update MainViewModel for PHP Debugging**
- Detect PHP projects (*.php files, composer.json)
- For CLI scripts: launch directly via DapClient.Launch()
- For web apps: may need special handling (e.g., start PHP server, configure Xdebug)
- Pass additional PHP-specific properties via DebugConfiguration.AdditionalProperties

---

## 6. Workflow: Adding New Debug Adapter (Template)

1. **Define Adapter Info**
   `csharp
   var newAdapter = new DebugAdapterInfo
   {
       Type = "{adapter_type}",                           // e.g., "go", "rust"
       DisplayName = "{Human Readable Name}",             // e.g., "Go (dlv)"
       ExecutableName = "{executable_name}",              // e.g., "dlv"
       Arguments = ["{arg1}", "{arg2}"],                  // e.g., ["exec"]
       SupportedRuntimes = ["{runtime1}", "{runtime2}"], // e.g., ["go"]
   };
   `

2. **Register in Dependency Container**
   - In App.axaml.cs ConfigureServices, create registry instance
   - Call dapterRegistry.Register(newAdapter)

3. **Optional: Implement Auto-Download**
   - Add method to DebugAdapterDownloader (pattern: EnsureXxxAsync())
   - Check installed, download from official source, extract, set permissions
   - Called from DebugService.ResolveAdapterPathAsync() with fallback

4. **Extend Launch Logic (if needed)**
   - In MainViewModel.StartDebugging(), add detection for language/project type
   - Construct DebugConfiguration with correct Type and Request
   - Handle language-specific pre-launch (build, start server, etc.)
   - Call DebugService.StartDebuggingAsync(config)

5. **Handle Adapter-Specific Features**
   - If adapter supports reverse requests (e.g., runInTerminal), already handled by DapClient
   - If adapter needs custom event handling, extend DebugService event handlers
   - If adapter uses non-standard DAP extensions, add to DapProtocolTypes.cs

---

## 7. Key Integration Points

### UI → Debug Service
- **Location:** src/NVS/ViewModels/MainViewModel.cs
- Main entry points: StartDebugging(), StopDebugging(), PauseDebugging(), ContinueDebugging()
- Injected via constructor as IDebugService

### Variables Panel Integration
- **Location:** src/NVS/ViewModels/Dock/VariablesToolViewModel.cs
- Displays variables at breakpoints
- Calls DebugService.GetVariablesAsync(), GetChildVariablesAsync()
- Updates on DebuggingPaused event

### Breakpoint Gutter Integration
- **Location:** Editor view model
- Manages visual breakpoint markers
- Calls BreakpointStore.ToggleBreakpoint() on gutter click
- Listens to BreakpointStore.BreakpointChanged event for UI updates
- Breakpoints automatically synced to adapter when debug session starts

### Terminal Integration
- **Location:** src/NVS.Services/Terminal/
- Used for console app debugging (hook-based attach)
- Receives debug output via terminal output stream

---

## 8. Key Classes & File Structure

`
NVS.Core/
├── Interfaces/
│   ├── IDebugService.cs          (main service interface, model types)
│   ├── IDapClient.cs              (low-level DAP client)
│   └── IBreakpointStore.cs        (breakpoint management)
└── Debug/
    ├── DapProtocolTypes.cs        (all DAP message types)
    └── DapMessage.cs              (base message types, converters)

NVS.Services/Debug/
├── DebugService.cs                (main implementation)
├── DapClient.cs                   (DAP protocol client)
├── DapTransport.cs                (Content-Length framing)
├── DebugAdapterRegistry.cs        (adapter registration & discovery)
├── DebugAdapterDownloader.cs      (auto-download & install)
└── BreakpointStore.cs             (breakpoint in-memory store)

NVS/
└── App.axaml.cs                   (dependency injection setup)
`

---

## 9. Testing

**Tests Location:** 	ests/NVS.Services.Tests/

- DapClientTests.cs - DapClient and DebugService integration tests
- DebugAdapterDownloaderTests.cs - Downloader tests

Key test patterns:
- Mock DapClient for DebugService tests
- Mock DapTransport for DapClient tests
- Test message serialization/deserialization
- Test state transitions (stopped, continued, terminated)

---

## 10. Future Enhancements

1. **Conditional Breakpoints** - Already in DAP types, needs UI support
2. **Function Breakpoints** - Supported by DAP, needs UI + language-specific impl
3. **Watch Expressions** - Evaluate expressions on demand
4. **Logpoints** - Log without stopping, DAP-supported feature
5. **Multi-threaded Debug UI** - Thread list, thread context switching
6. **Hot Reload** - Apply code changes during debugging (requires adapter support)
7. **Remote Debugging** - TCP connection to remote adapter (partially implemented via ServerPort)
8. **Language-Specific Adapters** - Java (follow pattern above), PHP, Go, Rust, etc.
9. **Custom Debug Protocol Extensions** - Extend DAP types for language-specific features
10. **Debug Configuration Files** - Load debug configs from .nvs/debug.json or .vscode/launch.json

---

## Summary

The NVS debug infrastructure is architecturally sound and well-designed for extensibility:

- **Separation of Concerns:** DAP protocol handling (DapClient/DapTransport) separate from application logic (DebugService)
- **Registry Pattern:** Adapters registered dynamically, no hardcoded adapter logic
- **Dependency Injection:** Clean service interfaces, mockable for testing
- **Event-Driven:** Reactive UI updates via events, no tight coupling
- **State Machine:** Clear session lifecycle (not debugging → debugging → paused → debugging/stopped)
- **Extensibility:** New adapters require minimal changes (register, optionally extend downloader, optionally extend launch logic)

To add Java/PHP support: **Register adapters, optionally implement downloaders, extend MainViewModel launch logic as needed.**

