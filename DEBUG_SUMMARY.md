# NVS Debug Infrastructure - Summary Report

## Executive Summary

The NVS IDE implements a **comprehensive, extensible Debug Adapter Protocol (DAP) infrastructure** currently supporting C#/.NET debugging. The architecture is **production-ready for adding Java and PHP debugging** with minimal changes.

**Key Findings:**
- ✅ Well-architected with clear separation of concerns
- ✅ Registry pattern enables dynamic adapter registration
- ✅ Auto-download infrastructure for adapters (netcoredbg model)
- ✅ Modular services (DebugService, DapClient, DebugAdapterRegistry)
- ✅ Event-driven architecture for reactive UI
- ✅ Breakpoint management system with verification tracking

---

## Architecture at a Glance

### Core Components

1. **IDebugService** - High-level debug operations interface
   - Entry point for UI (MainViewModel)
   - Manages session lifecycle
   - Handles execution control, variables, breakpoints
   - Events for UI reactivity

2. **DebugService** - Implementation
   - Orchestrates DAP protocol
   - Launches/kills adapter processes
   - Bridges application and adapter abstractions
   - ~606 lines, handles 90% of debug logic

3. **DapClient** - Low-level DAP protocol client
   - Request/response correlation
   - Asynchronous message listener
   - Event dispatching
   - ~380 lines, protocol-focused

4. **DapTransport** - Message framing
   - Content-Length protocol (identical to LSP)
   - Stream I/O with mutual exclusion
   - JSON serialization

5. **DebugAdapterRegistry** - Adapter discovery & registration
   - Maps adapter types to executable info
   - Finds executables on PATH or system locations
   - ~108 lines, trivial to extend

6. **DebugAdapterDownloader** - Auto-installation
   - Downloads adapter binaries from GitHub
   - Extracts (zip/tar.gz), sets permissions
   - Caches in ~/.nvs/tools/
   - Extensible for new adapters

7. **BreakpointStore** - In-memory breakpoint management
   - Per-file tracking
   - Thread-safe
   - Verification status
   - ~157 lines

---

## Current State: .NET/C# Support

**Adapter:** netcoredbg 3.1.3-1062
**Registration:** Hard-coded in DebugAdapterRegistry constructor
**Launch Modes:**
- **Console apps:** Attach mode with startup hook (pre-loads assembly)
- **GUI apps:** Direct launch via DAP

**Build Integration:**
- Pre-build check (dotnet build)
- Auto-download netcoredbg if missing
- Handles .NET Framework version detection

---

## Extension Points Identified

### 1. Adapter Registration (Lowest Effort)
**File:** src/NVS/App.axaml.cs (ConfigureServices)
**Action:** Call dapterRegistry.Register(newAdapterInfo)
**Time:** < 5 minutes
**Effort:** Trivial

### 2. Auto-Download (Low Effort)
**File:** src/NVS.Services/Debug/DebugAdapterDownloader.cs
**Action:** Add EnsureXxxAsync() method following netcoredbg pattern
**Time:** 30-60 minutes per adapter
**Effort:** Medium (need download URLs, extraction logic)

### 3. Launch Logic (Medium Effort)
**File:** src/NVS/ViewModels/MainViewModel.cs
**Action:** 
- Add project type detection (pom.xml, composer.json, etc.)
- Add language-specific build (Maven, Gradle, Composer)
- Create appropriate DebugConfiguration per language
**Time:** 1-2 hours
**Effort:** Medium (language-specific knowledge needed)

### 4. Configuration (Optional)
**File:** src/NVS.Core/Models/Settings/DebugConfiguration.cs
**Action:** Extend AdditionalProperties for language-specific options
**Time:** 30 minutes
**Effort:** Low (just add dictionary entries)

---

## What's Already in Place (Reusable)

✅ **DAP Message Types**
- All standard protocol messages defined (DapProtocolTypes.cs)
- Custom JSON converter for polymorphic deserialization
- Event routing infrastructure

✅ **Lifecycle Management**
- Session creation, tracking, cleanup
- Process spawning and termination
- Resource disposal patterns

✅ **State Tracking**
- IsDebugging, IsPaused flags
- Current thread/frame context
- Active session metadata

✅ **Event System**
- 8 events for UI reactivity
- Event handler subscription/unsubscription
- Output streaming

✅ **Error Handling**
- Graceful timeouts
- Best-effort cleanup
- DapRequestException for protocol errors

✅ **Threading**
- Background listener thread (DapClient)
- Semaphore-based mutual exclusion
- Concurrent request tracking

---

## Step-by-Step Implementation Path

### Phase 1: Quick Setup (Minimal Java/PHP Support)
**Time:** 30 minutes
**Output:** Java/PHP debugging works if adapters pre-installed on PATH

1. Register Java adapter
2. Register PHP adapter
3. Test with manual adapter path

### Phase 2: Auto-Download
**Time:** 1-2 hours
**Output:** One-click adapter installation

1. Implement EnsureJavaDebugAsync()
2. Implement EnsurePhpDebugAsync()
3. Update ResolveAdapterPathAsync()

### Phase 3: Smart Detection & Build
**Time:** 2-4 hours
**Output:** Seamless Java/PHP project debugging

1. Add project type detection (pom.xml, build.gradle, composer.json)
2. Add build logic (Maven, Gradle, Composer)
3. Update StartDebugging() for multi-language
4. Add unit tests

### Phase 4: Polish
**Time:** 1-2 hours
**Output:** Production-ready

1. Handle edge cases (missing main class, build failures)
2. Add logging and diagnostics
3. Update documentation
4. User testing

---

## Risk Assessment

**Low Risk Items:**
- Adapter registration (just data, no behavioral changes)
- Auto-download (proven pattern with netcoredbg)
- Event handling (already implemented for all languages)

**Medium Risk Items:**
- Project type detection (need accurate heuristics)
- Build integration (language-specific, can fail)
- Launch configuration (adapter-specific formats)

**Mitigation Strategies:**
- Start with Phase 1 (registration only)
- Test against real Java/PHP projects
- Add fallback manual configuration
- Implement retry logic for downloads

---

## File Structure Reference

`
src/
├── NVS.Core/
│   ├── Interfaces/
│   │   ├── IDebugService.cs ..................... Main service interface
│   │   ├── IDapClient.cs ........................ DAP client contract
│   │   └── IBreakpointStore.cs .................. Breakpoint contract
│   └── Debug/
│       ├── DapProtocolTypes.cs .................. 150+ DAP message types
│       └── DapMessage.cs ........................ Base message classes
│
├── NVS.Services/
│   └── Debug/
│       ├── DebugService.cs ...................... Main implementation (606 lines)
│       ├── DapClient.cs ......................... Protocol client (380 lines)
│       ├── DapTransport.cs ...................... Framing layer (~130 lines)
│       ├── DebugAdapterRegistry.cs .............. Adapter registration (108 lines)
│       ├── DebugAdapterDownloader.cs ............ Auto-download (225 lines)
│       └── BreakpointStore.cs .................. Breakpoint store (157 lines)
│
└── NVS/
    ├── App.axaml.cs ............................ DI setup (lines 122-158)
    └── ViewModels/
        ├── MainViewModel.cs .................... Debug UI orchestration (lines 860+)
        └── Dock/
            └── VariablesToolViewModel.cs ....... Variables panel integration
`

---

## Key Code Locations for Java/PHP

### To Add Java Adapter:

1. **Registration:**
   src/NVS/App.axaml.cs → ConfigureServices() → after line 145

2. **Auto-Download:**
   src/NVS.Services/Debug/DebugAdapterDownloader.cs → Add EnsureJavaDebugAsync()

3. **Resolver:**
   src/NVS.Services/Debug/DebugService.cs → ResolveAdapterPathAsync() → Add "java" case

4. **Detection:**
   src/NVS/ViewModels/MainViewModel.cs → StartDebugging() → Add Java case

### To Add PHP Adapter:

(Same pattern as Java, just with PHP-specific paths)

---

## Configuration Examples

### Java Debug Configuration
`csharp
new DebugConfiguration
{
    Name = "MyJavaApp",
    Type = "java",
    Request = "launch",
    Program = "/path/to/target/app.jar",
    Cwd = "/path/to/project",
    Args = ["--verbose"],
    AdditionalProperties = new Dictionary<string, object>
    {
        { "mainClass", "com.example.Main" },
        { "projectName", "my-java-app" },
        { "vmArgs", "-Xmx512m" },
    }
}
`

### PHP Debug Configuration
`csharp
new DebugConfiguration
{
    Name = "MyPHPApp",
    Type = "php",
    Request = "launch",
    Program = "/path/to/index.php",
    Cwd = "/path/to/project",
    AdditionalProperties = new Dictionary<string, object>
    {
        { "runtimeExecutable", "/usr/bin/php" },
        { "port", 9003 },
        { "xdebugSettings", new { 
            { "max_data", 65535 },
            { "show_hidden", 1 }
        }}
    }
}
`

---

## Testing Recommendations

1. **Unit Tests** (low effort, high value)
   - DebugAdapterRegistry.Register()
   - DebugAdapterDownloader.GetInstalled()
   - BreakpointStore operations

2. **Integration Tests** (medium effort)
   - DapClient message send/receive
   - DebugService lifecycle
   - Mock adapter via test transport

3. **System Tests** (high effort)
   - Real Java project with Maven/Gradle
   - Real PHP project
   - Set breakpoints, hit, inspect variables, step
   - Auto-download functionality

---

## Documentation to Create

1. ✅ **Architecture Document** (DEBUG_ARCHITECTURE.md)
   - Overview, layers, components, flow diagrams
   
2. ✅ **Implementation Guide** (DEBUG_IMPLEMENTATION_GUIDE.md)
   - Step-by-step for Java/PHP addition
   - Code examples, checklists
   
3. **Quick Start for Contributors**
   - How to add a new adapter (condensed)
   - Common pitfalls
   
4. **User Guide**
   - How to configure debugging for Java/PHP
   - Troubleshooting
   
5. **API Reference**
   - IDebugService methods
   - DebugConfiguration properties
   - Event specifications

---

## Estimated Effort Summary

| Phase | Task | Effort | Time |
|-------|------|--------|------|
| 1 | Register Java adapter | Trivial | 5 min |
| 1 | Register PHP adapter | Trivial | 5 min |
| 2 | EnsureJavaDebugAsync() | Medium | 1 hr |
| 2 | EnsurePhpDebugAsync() | Medium | 1 hr |
| 3 | Project detection | Medium | 1 hr |
| 3 | Build integration | Medium | 1.5 hrs |
| 3 | Launch logic | Medium | 1.5 hrs |
| 4 | Testing | Medium | 2 hrs |
| 4 | Documentation | Low | 1 hr |
| **Total** | **End-to-end Java & PHP support** | **Medium** | **~8-9 hrs** |

---

## Success Criteria

1. ✅ Java adapter registers and discovers successfully
2. ✅ PHP adapter registers and discovers successfully
3. ✅ Auto-download works for both adapters
4. ✅ Maven/Gradle projects auto-build before debug
5. ✅ Composer projects install dependencies before debug
6. ✅ Breakpoints set and hit in Java debugger
7. ✅ Variables inspectable in Java debug session
8. ✅ Breakpoints set and hit in PHP debugger
9. ✅ Variables inspectable in PHP debug session
10. ✅ Step operations (over, in, out) work
11. ✅ Execution control (pause, continue) works
12. ✅ Unit tests pass
13. ✅ Documentation complete

---

## Conclusion

The NVS debug infrastructure is **well-designed, maintainable, and ready for expansion.** Adding Java and PHP support requires:

1. **Minimal code changes** (~200-300 lines of new code)
2. **Clear extension points** (registry, downloader, UI)
3. **Proven patterns** (netcoredbg as reference)
4. **Good test coverage opportunities** (mock adapters)

**Recommendation:** Proceed with Phase 1 (registration) immediately. If successful and helpful, advance to Phase 2 (auto-download) and Phase 3 (smart detection).

