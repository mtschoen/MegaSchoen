# Claude Window Cycler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add a global hotkey to MegaSchoen that cycles focus through Claude cmd.exe sessions currently waiting on a permission prompt, using Claude Code's own `Notification` hook as the state source.

**Architecture:** Three new projects in the existing MegaSchoen solution: a `ClaudeCycler.Core` class library (state store, hook dispatch, process resolution), a `ClaudeHookBridge` console exe (hook handler + inspection CLI), and an MSTest project. MegaSchoen itself gets a `ClaudeWindowService`, a small extension to `GlobalHotkeyService` for non-profile hotkeys, a tray menu item for one-time hook installation, and a post-build copy of the bridge exe.

**Tech Stack:** .NET 10, MAUI (Windows target), MSTest, Win32 P/Invoke (`user32`, `ntdll`, `kernel32`), `System.Management` (WMI), `System.Text.Json`.

**Reference spec:** `docs/superpowers/specs/2026-04-18-claude-window-cycler-design.md`

**Coding conventions (from repo CLAUDE.md):**
- `var` everywhere, file-scoped namespaces, expression-bodied members, braces always
- Omit default access modifiers (`private` for members, `internal` for types)
- Omit `this.`, use language keywords (`string`, `int`), use pattern matching / null propagation
- Private fields: `_camelCase`. Interfaces: `IName`. Types/methods/properties: `PascalCase`
- **Build command:** `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64` (NOT `dotnet build`)

**Repo layout after this plan:**

```
ClaudeCycler.Core/                     (new)
  ClaudeCycler.Core.csproj
  Paths.cs
  Logger.cs
  Models/
    SessionEntry.cs
    NeedySessionsFile.cs
    HookPayload.cs
  StateStore.cs
  HookDispatcher.cs
  SettingsJsonInstaller.cs
  ProcessResolver.cs
  Interop/
    NtDll.cs
    User32.cs
    Kernel32.cs
ClaudeCycler.Core.Tests/               (new)
  ClaudeCycler.Core.Tests.csproj
  PathsTests.cs
  StateStoreTests.cs
  HookDispatcherTests.cs
  SettingsJsonInstallerTests.cs
ClaudeHookBridge/                      (new)
  ClaudeHookBridge.csproj
  Program.cs
  Commands/
    StatusCommand.cs
    LogsCommand.cs
    CheckCommand.cs
    ResolveCommand.cs
MegaSchoen/Platforms/Windows/Services/
  ClaudeWindowService.cs               (new)
  Win32ForegroundHelper.cs             (new)
  GlobalHotkeyService.cs               (modified)
  TrayIconService.cs                   (modified)
  Win32Interop.cs                      (modified)
MegaSchoen/MauiProgram.cs              (modified)
MegaSchoen/Platforms/Windows/App.xaml.cs  (modified)
MegaSchoen/MegaSchoen.csproj           (modified — add ref + post-build copy)
MegaSchoen.sln                         (modified — add new projects)
```

**v1 scope decisions:**

- Hotkey is **hardcoded to `Ctrl+Alt+Shift+C`** in v1. Making it user-configurable is a follow-up paired with the GUI tab.
- `logs -f` (tail mode) is deferred; v1 prints log contents once and exits.
- Cycler handles **only cmd.exe** parent terminals. PowerShell / Windows Terminal / WSL are follow-ups.

---

## Task 1: Create ClaudeCycler.Core project

**Files:**
- Create: `ClaudeCycler.Core/ClaudeCycler.Core.csproj`
- Modify: `MegaSchoen.sln`

- [x] **Step 1: Create csproj**

`ClaudeCycler.Core/ClaudeCycler.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Management" Version="10.0.0" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add project to solution**

Run: `dotnet sln "C:\Users\mtsch\source\repos\MegaSchoen\MegaSchoen.sln" add "C:\Users\mtsch\source\repos\MegaSchoen\ClaudeCycler.Core\ClaudeCycler.Core.csproj"`

- [x] **Step 3: Build and verify**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64 -t:ClaudeCycler_Core:Build`
Expected: build succeeds.

- [x] **Step 4: Commit**

```bash
git add ClaudeCycler.Core/ClaudeCycler.Core.csproj MegaSchoen.sln
git commit -m "Add ClaudeCycler.Core project"
```

---

## Task 2: Create ClaudeCycler.Core.Tests project

**Files:**
- Create: `ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`
- Modify: `MegaSchoen.sln`

- [x] **Step 1: Create csproj**

`ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MSTest" Version="4.0.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add to solution**

Run: `dotnet sln "C:\Users\mtsch\source\repos\MegaSchoen\MegaSchoen.sln" add "C:\Users\mtsch\source\repos\MegaSchoen\ClaudeCycler.Core.Tests\ClaudeCycler.Core.Tests.csproj"`

- [x] **Step 3: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Expected: build succeeds.

- [x] **Step 4: Commit**

```bash
git add ClaudeCycler.Core.Tests/ MegaSchoen.sln
git commit -m "Add ClaudeCycler.Core.Tests project"
```

---

## Task 3: Paths helper

**Files:**
- Create: `ClaudeCycler.Core/Paths.cs`
- Test: `ClaudeCycler.Core.Tests/PathsTests.cs`

- [x] **Step 1: Write failing test**

`ClaudeCycler.Core.Tests/PathsTests.cs`:

```csharp
namespace ClaudeCycler.Core.Tests;

[TestClass]
public class PathsTests
{
    [TestMethod]
    public void AppDataDirectory_IsUnderLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.AreEqual(Path.Combine(localAppData, "MegaSchoen"), Paths.AppDataDirectory);
    }

    [TestMethod]
    public void NeedySessionsFile_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "needy-sessions.json"), Paths.NeedySessionsFile);
    }

    [TestMethod]
    public void HookBridgeLog_IsUnderAppDataDirectory()
    {
        Assert.AreEqual(Path.Combine(Paths.AppDataDirectory, "hook-bridge.log"), Paths.HookBridgeLog);
    }

    [TestMethod]
    public void ClaudeSettingsFile_IsUnderUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.AreEqual(Path.Combine(userProfile, ".claude", "settings.json"), Paths.ClaudeSettingsFile);
    }
}
```

- [x] **Step 2: Run — verify failure**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`
Expected: compile error (`Paths` does not exist).

- [x] **Step 3: Implement**

`ClaudeCycler.Core/Paths.cs`:

```csharp
namespace ClaudeCycler.Core;

public static class Paths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MegaSchoen");

    public static string NeedySessionsFile { get; } =
        Path.Combine(AppDataDirectory, "needy-sessions.json");

    public static string HookBridgeLog { get; } =
        Path.Combine(AppDataDirectory, "hook-bridge.log");

    public static string ClaudeSettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static void EnsureAppDataDirectoryExists() =>
        Directory.CreateDirectory(AppDataDirectory);
}
```

- [x] **Step 4: Run — verify pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`
Expected: 4 passed.

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/Paths.cs ClaudeCycler.Core.Tests/PathsTests.cs
git commit -m "Add Paths helper for ClaudeCycler"
```

---

## Task 4: Data models

**Files:**
- Create: `ClaudeCycler.Core/Models/SessionEntry.cs`
- Create: `ClaudeCycler.Core/Models/NeedySessionsFile.cs`
- Create: `ClaudeCycler.Core/Models/HookPayload.cs`

- [x] **Step 1: Implement models**

`ClaudeCycler.Core/Models/SessionEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class SessionEntry
{
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("notifiedAt")]
    public DateTimeOffset NotifiedAt { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
```

`ClaudeCycler.Core/Models/NeedySessionsFile.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class NeedySessionsFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sessions")]
    public Dictionary<string, SessionEntry> Sessions { get; set; } = new();
}
```

`ClaudeCycler.Core/Models/HookPayload.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class HookPayload
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("transcript_path")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; set; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
```

- [x] **Step 2: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Expected: build succeeds.

- [x] **Step 3: Commit**

```bash
git add ClaudeCycler.Core/Models/
git commit -m "Add ClaudeCycler data models"
```

---

## Task 5: Logger

**Files:**
- Create: `ClaudeCycler.Core/Logger.cs`

- [x] **Step 1: Implement**

Logger is small enough and has filesystem side-effects; we smoke-test via HookDispatcher tests rather than isolate.

`ClaudeCycler.Core/Logger.cs`:

```csharp
namespace ClaudeCycler.Core;

public static class Logger
{
    static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            Paths.EnsureAppDataDirectoryExists();
            var line = $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(Paths.HookBridgeLog, line);
            }
        }
        catch
        {
            // Never throw from logging — this runs inside hook handlers.
        }
    }
}
```

- [x] **Step 2: Build + commit**

```bash
git add ClaudeCycler.Core/Logger.cs
git commit -m "Add Logger for hook bridge"
```

---

## Task 6: StateStore — read

**Files:**
- Create: `ClaudeCycler.Core/StateStore.cs`
- Test: `ClaudeCycler.Core.Tests/StateStoreTests.cs`

- [x] **Step 1: Write failing test**

`ClaudeCycler.Core.Tests/StateStoreTests.cs`:

```csharp
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class StateStoreTests
{
    string _tempFile = "";

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [TestMethod]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
        Assert.AreEqual(1, file.Version);
    }

    [TestMethod]
    public void Read_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "{ not valid json");
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Read_ValidFile_Parses()
    {
        File.WriteAllText(_tempFile, """
        {
          "version": 1,
          "sessions": {
            "abc": { "cwd": "C:\\foo", "notifiedAt": "2026-04-18T12:00:00Z", "message": "hi" }
          }
        }
        """);
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(1, file.Sessions.Count);
        Assert.AreEqual("C:\\foo", file.Sessions["abc"].Cwd);
        Assert.AreEqual("hi", file.Sessions["abc"].Message);
    }
}
```

- [x] **Step 2: Run — verify failure**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~StateStoreTests"`
Expected: compile error (`StateStore` does not exist).

- [x] **Step 3: Implement read**

`ClaudeCycler.Core/StateStore.cs`:

```csharp
using System.Text.Json;
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class StateStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly string _path;
    readonly object _lock = new();

    public StateStore(string path)
    {
        _path = path;
    }

    public StateStore() : this(Paths.NeedySessionsFile) { }

    public NeedySessionsFile Read()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new NeedySessionsFile();
            }

            try
            {
                var text = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<NeedySessionsFile>(text) ?? new NeedySessionsFile();
            }
            catch (Exception exception)
            {
                Logger.Log($"StateStore.Read failed: {exception.Message}");
                return new NeedySessionsFile();
            }
        }
    }
}
```

- [x] **Step 4: Run — verify pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~StateStoreTests"`
Expected: 3 passed.

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/StateStore.cs ClaudeCycler.Core.Tests/StateStoreTests.cs
git commit -m "Add StateStore.Read with corruption recovery"
```

---

## Task 7: StateStore — atomic write + upsert + delete

**Files:**
- Modify: `ClaudeCycler.Core/StateStore.cs`
- Modify: `ClaudeCycler.Core.Tests/StateStoreTests.cs`

- [x] **Step 1: Add failing tests**

Append to `StateStoreTests.cs`:

```csharp
    [TestMethod]
    public void Upsert_NewSession_PersistsEntry()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow, Message = "hi" });

        var file = store.Read();
        Assert.AreEqual(1, file.Sessions.Count);
        Assert.AreEqual("C:\\foo", file.Sessions["sess1"].Cwd);
    }

    [TestMethod]
    public void Upsert_ExistingSession_Overwrites()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\bar", NotifiedAt = DateTimeOffset.UtcNow });

        var file = store.Read();
        Assert.AreEqual("C:\\bar", file.Sessions["sess1"].Cwd);
    }

    [TestMethod]
    public void Delete_ExistingSession_RemovesEntry()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Delete("sess1");

        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Delete_MissingSession_IsNoop()
    {
        var store = new StateStore(_tempFile);
        store.Delete("nope"); // should not throw
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Write_UsesTempFileThenRename()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });

        Assert.IsTrue(File.Exists(_tempFile));
        Assert.IsFalse(File.Exists(_tempFile + ".tmp"), "tmp file should have been renamed away");
    }
```

- [x] **Step 2: Run — verify failure**

Expected: compile errors — `Upsert` / `Delete` / `Write` missing.

- [x] **Step 3: Implement**

Add to `StateStore.cs` inside the class:

```csharp
    public void Write(NeedySessionsFile file)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
    }

    public void Upsert(string sessionId, SessionEntry entry)
    {
        var file = Read();
        file.Sessions[sessionId] = entry;
        Write(file);
    }

    public void Delete(string sessionId)
    {
        var file = Read();
        if (file.Sessions.Remove(sessionId))
        {
            Write(file);
        }
    }
```

- [x] **Step 4: Run — verify pass**

Expected: 8 passed.

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/StateStore.cs ClaudeCycler.Core.Tests/StateStoreTests.cs
git commit -m "Add StateStore atomic write + upsert + delete"
```

---

## Task 8: StateStore — stale cutoff filter

**Files:**
- Modify: `ClaudeCycler.Core/StateStore.cs`
- Modify: `ClaudeCycler.Core.Tests/StateStoreTests.cs`

- [x] **Step 1: Write failing test**

Append:

```csharp
    [TestMethod]
    public void ReadFresh_OmitsEntriesOlderThanCutoff()
    {
        var store = new StateStore(_tempFile);
        var now = DateTimeOffset.UtcNow;
        store.Upsert("fresh", new SessionEntry { Cwd = "C:\\a", NotifiedAt = now });
        store.Upsert("stale", new SessionEntry { Cwd = "C:\\b", NotifiedAt = now - TimeSpan.FromMinutes(45) });

        var file = store.ReadFresh(TimeSpan.FromMinutes(30));

        Assert.AreEqual(1, file.Sessions.Count);
        Assert.IsTrue(file.Sessions.ContainsKey("fresh"));
        Assert.IsFalse(file.Sessions.ContainsKey("stale"));
    }
```

- [x] **Step 2: Run — fails**

- [x] **Step 3: Implement**

Add to `StateStore.cs`:

```csharp
    public NeedySessionsFile ReadFresh(TimeSpan maxAge)
    {
        var file = Read();
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var filtered = new NeedySessionsFile { Version = file.Version };
        foreach (var (id, entry) in file.Sessions)
        {
            if (entry.NotifiedAt >= cutoff)
            {
                filtered.Sessions[id] = entry;
            }
        }
        return filtered;
    }
```

- [x] **Step 4: Run — pass**

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/StateStore.cs ClaudeCycler.Core.Tests/StateStoreTests.cs
git commit -m "Add StateStore stale-entry filter"
```

---

## Task 9: HookDispatcher

**Files:**
- Create: `ClaudeCycler.Core/HookDispatcher.cs`
- Test: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [x] **Step 1: Write failing tests**

`ClaudeCycler.Core.Tests/HookDispatcherTests.cs`:

```csharp
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class HookDispatcherTests
{
    string _tempFile = "";
    StateStore _store = null!;
    HookDispatcher _dispatcher = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
        _store = new StateStore(_tempFile);
        _dispatcher = new HookDispatcher(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [TestMethod]
    public void Notification_PermissionPrompt_UpsertsSession()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "permission_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo",
            Message = "Claude needs your permission"
        });

        var file = _store.Read();
        Assert.IsTrue(file.Sessions.ContainsKey("s1"));
        Assert.AreEqual("C:\\foo", file.Sessions["s1"].Cwd);
        Assert.AreEqual("Claude needs your permission", file.Sessions["s1"].Message);
    }

    [TestMethod]
    public void Notification_IdlePrompt_DoesNotUpsert()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "idle_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void UserPromptSubmit_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "UserPromptSubmit", SessionId = "s1" });

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void Stop_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "Stop", SessionId = "s1" });

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void UnknownEvent_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload { HookEventName = "SomeOtherEvent", SessionId = "s1" });
        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void MissingSessionId_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "permission_prompt"
            // SessionId deliberately null
        });
        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }
}
```

- [x] **Step 2: Run — fails (compile errors)**

- [x] **Step 3: Implement**

`ClaudeCycler.Core/HookDispatcher.cs`:

```csharp
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class HookDispatcher
{
    readonly StateStore _store;

    public HookDispatcher(StateStore store)
    {
        _store = store;
    }

    public void Dispatch(HookPayload payload)
    {
        if (string.IsNullOrEmpty(payload.SessionId))
        {
            Logger.Log($"HookDispatcher: missing session_id for event {payload.HookEventName}");
            return;
        }

        try
        {
            switch (payload.HookEventName)
            {
                case "Notification" when payload.NotificationType == "permission_prompt":
                    _store.Upsert(payload.SessionId, new SessionEntry
                    {
                        Cwd = payload.Cwd ?? "",
                        NotifiedAt = DateTimeOffset.UtcNow,
                        Message = payload.Message
                    });
                    break;

                case "UserPromptSubmit":
                case "Stop":
                    _store.Delete(payload.SessionId);
                    break;

                default:
                    Logger.Log($"HookDispatcher: ignoring event {payload.HookEventName} / type {payload.NotificationType}");
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"HookDispatcher.Dispatch failed: {exception.Message}");
        }
    }
}
```

- [x] **Step 4: Run — pass**

Expected: 6 passed.

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "Add HookDispatcher routing notification/submit/stop events"
```

---

## Task 10: Create ClaudeHookBridge executable project

**Files:**
- Create: `ClaudeHookBridge/ClaudeHookBridge.csproj`
- Create: `ClaudeHookBridge/Program.cs` (stub)
- Modify: `MegaSchoen.sln`

- [x] **Step 1: Create csproj**

`ClaudeHookBridge/ClaudeHookBridge.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ClaudeHookBridge</RootNamespace>
    <AssemblyName>ClaudeHookBridge</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Create stub Program.cs**

```csharp
namespace ClaudeHookBridge;

public static class Program
{
    public static int Main(string[] arguments) => 0;
}
```

- [x] **Step 3: Add to solution and build**

Run: `dotnet sln "C:\Users\mtsch\source\repos\MegaSchoen\MegaSchoen.sln" add "C:\Users\mtsch\source\repos\MegaSchoen\ClaudeHookBridge\ClaudeHookBridge.csproj"`
Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Expected: both new projects build.

- [x] **Step 4: Commit**

```bash
git add ClaudeHookBridge/ MegaSchoen.sln
git commit -m "Add ClaudeHookBridge executable project (stub)"
```

---

## Task 11: Program.cs — hook mode (stdin → dispatcher)

**Files:**
- Modify: `ClaudeHookBridge/Program.cs`
- Test: `ClaudeCycler.Core.Tests/HookModeIntegrationTests.cs`

- [x] **Step 1: Write failing integration test**

`ClaudeCycler.Core.Tests/HookModeIntegrationTests.cs`:

```csharp
using System.Diagnostics;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class HookModeIntegrationTests
{
    static string BridgeExePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "ClaudeHookBridge", "bin", "Debug", "net10.0-windows10.0.26100.0",
            "ClaudeHookBridge.exe");

    [TestMethod]
    public void StdinPermissionPrompt_UpsertsStateFile()
    {
        // Point at a temp state file by overriding LOCALAPPDATA for the child process
        var tempLocalAppData = Path.Combine(Path.GetTempPath(), $"megaschoen-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempLocalAppData);
        try
        {
            var payload = """
            {
              "hook_event_name": "Notification",
              "notification_type": "permission_prompt",
              "session_id": "integration-s1",
              "cwd": "C:\\foo",
              "message": "Claude needs your permission"
            }
            """;

            var startInfo = new ProcessStartInfo(BridgeExePath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.EnvironmentVariables["LOCALAPPDATA"] = tempLocalAppData;

            using var process = Process.Start(startInfo)!;
            process.StandardInput.Write(payload);
            process.StandardInput.Close();
            process.WaitForExit(5000);

            Assert.AreEqual(0, process.ExitCode);

            var stateFile = Path.Combine(tempLocalAppData, "MegaSchoen", "needy-sessions.json");
            Assert.IsTrue(File.Exists(stateFile), "state file was not created");
            var contents = File.ReadAllText(stateFile);
            StringAssert.Contains(contents, "integration-s1");
        }
        finally
        {
            if (Directory.Exists(tempLocalAppData)) Directory.Delete(tempLocalAppData, recursive: true);
        }
    }
}
```

- [x] **Step 2: Implement Program.cs hook mode**

Replace `ClaudeHookBridge/Program.cs`:

```csharp
using System.Text.Json;
using ClaudeCycler.Core;
using ClaudeCycler.Core.Models;

namespace ClaudeHookBridge;

public static class Program
{
    public static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0)
            {
                return RunHookMode();
            }

            // Inspection-mode dispatch added in later tasks.
            Console.Error.WriteLine($"Unknown subcommand: {arguments[0]}");
            return 1;
        }
        catch (Exception exception)
        {
            Logger.Log($"Program.Main unhandled: {exception}");
            return 0; // hook mode must never fail Claude
        }
    }

    static int RunHookMode()
    {
        try
        {
            var stdin = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(stdin))
            {
                Logger.Log("Hook mode: empty stdin");
                return 0;
            }

            var payload = JsonSerializer.Deserialize<HookPayload>(stdin);
            if (payload is null)
            {
                Logger.Log("Hook mode: null payload after deserialization");
                return 0;
            }

            var dispatcher = new HookDispatcher(new StateStore());
            dispatcher.Dispatch(payload);
            return 0;
        }
        catch (Exception exception)
        {
            Logger.Log($"RunHookMode failed: {exception.Message}");
            return 0;
        }
    }
}
```

- [x] **Step 3: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 4: Run test**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~HookModeIntegration"`
Expected: pass.

- [x] **Step 5: Commit**

```bash
git add ClaudeHookBridge/Program.cs ClaudeCycler.Core.Tests/HookModeIntegrationTests.cs
git commit -m "Wire ClaudeHookBridge hook mode to HookDispatcher"
```

---

## Task 12: Status subcommand

**Files:**
- Create: `ClaudeHookBridge/Commands/StatusCommand.cs`
- Modify: `ClaudeHookBridge/Program.cs`

- [x] **Step 1: Implement StatusCommand**

`ClaudeHookBridge/Commands/StatusCommand.cs`:

```csharp
using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class StatusCommand
{
    public static int Run()
    {
        var store = new StateStore();
        var file = store.Read();
        var now = DateTimeOffset.UtcNow;
        var staleCutoff = TimeSpan.FromMinutes(30);

        Console.WriteLine($"State file: {Paths.NeedySessionsFile}");
        Console.WriteLine($"Version: {file.Version}");
        Console.WriteLine($"Sessions: {file.Sessions.Count}");
        Console.WriteLine();

        foreach (var (id, entry) in file.Sessions)
        {
            var age = now - entry.NotifiedAt;
            var stale = age > staleCutoff ? " [STALE]" : "";
            Console.WriteLine($"  {id}{stale}");
            Console.WriteLine($"    cwd:        {entry.Cwd}");
            Console.WriteLine($"    notifiedAt: {entry.NotifiedAt:O} ({age.TotalMinutes:F1} min ago)");
            Console.WriteLine($"    message:    {entry.Message ?? "(none)"}");
        }

        return 0;
    }
}
```

- [x] **Step 2: Wire into Program.cs**

Replace the "Unknown subcommand" branch:

```csharp
            return arguments[0] switch
            {
                "status" => Commands.StatusCommand.Run(),
                _ => PrintUnknownCommand(arguments[0])
            };
```

Add helper method to `Program`:

```csharp
    static int PrintUnknownCommand(string name)
    {
        Console.Error.WriteLine($"Unknown subcommand: {name}");
        Console.Error.WriteLine("Available: status, logs, check, resolve");
        return 1;
    }
```

- [x] **Step 3: Manual verification**

Run: `ClaudeHookBridge\bin\Debug\net10.0-windows10.0.26100.0\ClaudeHookBridge.exe status`
Expected: prints "Sessions: 0" (file likely missing or empty; should not crash).

- [x] **Step 4: Commit**

```bash
git add ClaudeHookBridge/Commands/StatusCommand.cs ClaudeHookBridge/Program.cs
git commit -m "Add ClaudeHookBridge status subcommand"
```

---

## Task 13: Logs subcommand

**Files:**
- Create: `ClaudeHookBridge/Commands/LogsCommand.cs`
- Modify: `ClaudeHookBridge/Program.cs`

- [x] **Step 1: Implement**

`ClaudeHookBridge/Commands/LogsCommand.cs`:

```csharp
using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class LogsCommand
{
    public static int Run()
    {
        if (!File.Exists(Paths.HookBridgeLog))
        {
            Console.WriteLine($"(no log file at {Paths.HookBridgeLog})");
            return 0;
        }

        using var stream = new FileStream(Paths.HookBridgeLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
        return 0;
    }
}
```

- [x] **Step 2: Wire into Program.cs**

Add `"logs" => Commands.LogsCommand.Run(),` to the switch.

- [x] **Step 3: Commit**

```bash
git add ClaudeHookBridge/Commands/LogsCommand.cs ClaudeHookBridge/Program.cs
git commit -m "Add ClaudeHookBridge logs subcommand"
```

---

## Task 14: SettingsJsonInstaller

**Files:**
- Create: `ClaudeCycler.Core/SettingsJsonInstaller.cs`
- Test: `ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs`

- [x] **Step 1: Write failing tests**

`ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs`:

```csharp
namespace ClaudeCycler.Core.Tests;

[TestClass]
public class SettingsJsonInstallerTests
{
    string _tempSettings = "";

    [TestInitialize]
    public void Setup()
    {
        _tempSettings = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempSettings)) File.Delete(_tempSettings);
        if (File.Exists(_tempSettings + ".bak")) File.Delete(_tempSettings + ".bak");
    }

    [TestMethod]
    public void Install_MissingFile_CreatesWithThreeHooks()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        Assert.IsTrue(File.Exists(_tempSettings));
        var contents = File.ReadAllText(_tempSettings);
        StringAssert.Contains(contents, "Notification");
        StringAssert.Contains(contents, "UserPromptSubmit");
        StringAssert.Contains(contents, "Stop");
        StringAssert.Contains(contents, "C:\\\\bridge.exe");
    }

    [TestMethod]
    public void Install_PreservesUnrelatedFields()
    {
        File.WriteAllText(_tempSettings, """
        { "permissions": { "allow": ["Bash(*:*)"] } }
        """);

        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        var contents = File.ReadAllText(_tempSettings);
        StringAssert.Contains(contents, "permissions");
        StringAssert.Contains(contents, "Bash(*:*)");
    }

    [TestMethod]
    public void Install_CreatesBackup()
    {
        File.WriteAllText(_tempSettings, "{}");
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");

        Assert.IsTrue(File.Exists(_tempSettings + ".bak"));
    }

    [TestMethod]
    public void Install_Idempotent_DoesNotDuplicate()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\bridge.exe");
        installer.Install("C:\\bridge.exe");

        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.InstalledHere, status.Notification);
        Assert.AreEqual(InstallState.InstalledHere, status.UserPromptSubmit);
        Assert.AreEqual(InstallState.InstalledHere, status.Stop);
    }

    [TestMethod]
    public void GetStatus_DifferentPath_ReportsElsewhere()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        installer.Install("C:\\other.exe");

        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.InstalledElsewhere, status.Notification);
    }

    [TestMethod]
    public void GetStatus_MissingFile_ReportsNotInstalled()
    {
        var installer = new SettingsJsonInstaller(_tempSettings);
        var status = installer.GetStatus("C:\\bridge.exe");
        Assert.AreEqual(InstallState.NotInstalled, status.Notification);
    }
}
```

- [x] **Step 2: Run — fails (types missing)**

- [x] **Step 3: Implement**

`ClaudeCycler.Core/SettingsJsonInstaller.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeCycler.Core;

public enum InstallState
{
    NotInstalled,
    InstalledHere,
    InstalledElsewhere
}

public sealed class EventInstallStatus
{
    public InstallState Notification { get; set; }
    public InstallState UserPromptSubmit { get; set; }
    public InstallState Stop { get; set; }

    public string? NotificationPath { get; set; }
    public string? UserPromptSubmitPath { get; set; }
    public string? StopPath { get; set; }
}

public sealed class SettingsJsonInstaller
{
    static readonly string[] EventNames = { "Notification", "UserPromptSubmit", "Stop" };
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    readonly string _settingsPath;

    public SettingsJsonInstaller(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public SettingsJsonInstaller() : this(Paths.ClaudeSettingsFile) { }

    public void Install(string bridgeExePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        JsonObject root;
        if (File.Exists(_settingsPath))
        {
            File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true);
            var existing = File.ReadAllText(_settingsPath);
            root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (!root.ContainsKey("hooks"))
        {
            root["hooks"] = new JsonObject();
        }
        var hooksObj = root["hooks"]!.AsObject();

        foreach (var eventName in EventNames)
        {
            if (!hooksObj.ContainsKey(eventName))
            {
                hooksObj[eventName] = new JsonArray();
            }
            var eventArray = hooksObj[eventName]!.AsArray();

            var alreadyInstalled = false;
            foreach (var group in eventArray)
            {
                if (group is JsonObject groupObj && groupObj["hooks"] is JsonArray handlers)
                {
                    foreach (var handler in handlers)
                    {
                        if (handler is JsonObject h
                            && h["type"]?.GetValue<string>() == "command"
                            && PathsEqual(h["command"]?.GetValue<string>(), bridgeExePath))
                        {
                            alreadyInstalled = true;
                            break;
                        }
                    }
                }
                if (alreadyInstalled) break;
            }

            if (!alreadyInstalled)
            {
                eventArray.Add(new JsonObject
                {
                    ["hooks"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = bridgeExePath
                    })
                });
            }
        }

        File.WriteAllText(_settingsPath, root.ToJsonString(Options));
    }

    public EventInstallStatus GetStatus(string bridgeExePath)
    {
        var status = new EventInstallStatus();

        if (!File.Exists(_settingsPath))
        {
            return status;
        }

        var root = JsonNode.Parse(File.ReadAllText(_settingsPath))?.AsObject();
        if (root is null || root["hooks"] is not JsonObject hooksObj)
        {
            return status;
        }

        foreach (var eventName in EventNames)
        {
            var (state, path) = EvaluateEvent(hooksObj, eventName, bridgeExePath);
            switch (eventName)
            {
                case "Notification":
                    status.Notification = state;
                    status.NotificationPath = path;
                    break;
                case "UserPromptSubmit":
                    status.UserPromptSubmit = state;
                    status.UserPromptSubmitPath = path;
                    break;
                case "Stop":
                    status.Stop = state;
                    status.StopPath = path;
                    break;
            }
        }

        return status;
    }

    static (InstallState, string?) EvaluateEvent(JsonObject hooksObj, string eventName, string bridgeExePath)
    {
        if (hooksObj[eventName] is not JsonArray eventArray || eventArray.Count == 0)
        {
            return (InstallState.NotInstalled, null);
        }

        string? firstCommandPath = null;
        foreach (var group in eventArray)
        {
            if (group is JsonObject groupObj && groupObj["hooks"] is JsonArray handlers)
            {
                foreach (var handler in handlers)
                {
                    if (handler is JsonObject h && h["type"]?.GetValue<string>() == "command")
                    {
                        var commandPath = h["command"]?.GetValue<string>();
                        firstCommandPath ??= commandPath;
                        if (PathsEqual(commandPath, bridgeExePath))
                        {
                            return (InstallState.InstalledHere, commandPath);
                        }
                    }
                }
            }
        }

        return (InstallState.InstalledElsewhere, firstCommandPath);
    }

    static bool PathsEqual(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 4: Run — pass**

Expected: 6 passed.

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/SettingsJsonInstaller.cs ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs
git commit -m "Add SettingsJsonInstaller with backup + idempotent merge"
```

---

## Task 15: Check subcommand

**Files:**
- Create: `ClaudeHookBridge/Commands/CheckCommand.cs`
- Modify: `ClaudeHookBridge/Program.cs`

- [x] **Step 1: Implement**

`ClaudeHookBridge/Commands/CheckCommand.cs`:

```csharp
using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class CheckCommand
{
    public static int Run()
    {
        var bridgeExePath = Environment.ProcessPath ?? "";
        var installer = new SettingsJsonInstaller();
        var status = installer.GetStatus(bridgeExePath);

        Console.WriteLine($"Bridge exe:     {bridgeExePath}");
        Console.WriteLine($"Settings file:  {Paths.ClaudeSettingsFile}");
        Console.WriteLine();
        Print("Notification",     status.Notification,     status.NotificationPath);
        Print("UserPromptSubmit", status.UserPromptSubmit, status.UserPromptSubmitPath);
        Print("Stop",             status.Stop,             status.StopPath);

        var allInstalled =
            status.Notification == InstallState.InstalledHere &&
            status.UserPromptSubmit == InstallState.InstalledHere &&
            status.Stop == InstallState.InstalledHere;

        return allInstalled ? 0 : 2;
    }

    static void Print(string eventName, InstallState state, string? path)
    {
        var label = state switch
        {
            InstallState.InstalledHere => "INSTALLED (this binary)",
            InstallState.InstalledElsewhere => $"INSTALLED AT DIFFERENT PATH: {path}",
            _ => "NOT INSTALLED"
        };
        Console.WriteLine($"  {eventName,-18} {label}");
    }
}
```

- [x] **Step 2: Wire into Program.cs**

Add `"check" => Commands.CheckCommand.Run(),` to the switch.

- [x] **Step 3: Manual verify**

Run: `ClaudeHookBridge\bin\Debug\net10.0-windows10.0.26100.0\ClaudeHookBridge.exe check`
Expected: prints all three events as NOT INSTALLED, exit code 2.

- [x] **Step 4: Commit**

```bash
git add ClaudeHookBridge/Commands/CheckCommand.cs ClaudeHookBridge/Program.cs
git commit -m "Add ClaudeHookBridge check subcommand"
```

---

## Task 16: Win32 interop declarations for process resolution

**Files:**
- Create: `ClaudeCycler.Core/Interop/User32.cs`
- Create: `ClaudeCycler.Core/Interop/Kernel32.cs`
- Create: `ClaudeCycler.Core/Interop/NtDll.cs`

- [x] **Step 1: Create User32**

`ClaudeCycler.Core/Interop/User32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ClaudeCycler.Core.Interop;

public static partial class User32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(IntPtr hWnd, [Out] char[] buffer, int maxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextLengthW(IntPtr hWnd);
}
```

- [x] **Step 2: Create Kernel32**

`ClaudeCycler.Core/Interop/Kernel32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ClaudeCycler.Core.Interop;

public static partial class Kernel32
{
    public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int PROCESS_VM_READ = 0x0010;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, void* buffer, nuint size, out nuint bytesRead);
}
```

- [x] **Step 3: Create NtDll**

`ClaudeCycler.Core/Interop/NtDll.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ClaudeCycler.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct ProcessBasicInformation
{
    public int ExitStatus;
    public IntPtr PebBaseAddress;
    public IntPtr AffinityMask;
    public int BasePriority;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}

public static partial class NtDll
{
    public const int PROCESSBASICINFORMATION = 0;

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(IntPtr processHandle, int infoClass, ref ProcessBasicInformation info, int size, out int returnLength);
}
```

- [x] **Step 4: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/Interop/
git commit -m "Add Win32/Nt interop declarations for process enumeration"
```

---

## Task 17: ProcessResolver — read cmd.exe cwd via PEB

**Files:**
- Create: `ClaudeCycler.Core/ProcessResolver.cs`

- [x] **Step 1: Write the PEB-reading pipeline**

`ClaudeCycler.Core/ProcessResolver.cs`:

```csharp
using System.Management;
using System.Runtime.InteropServices;
using ClaudeCycler.Core.Interop;

namespace ClaudeCycler.Core;

public readonly record struct CmdWindow(uint ProcessId, IntPtr WindowHandle, string WindowTitle, string? WorkingDirectory);

public static class ProcessResolver
{
    public static List<CmdWindow> EnumerateCmdExeWindows()
    {
        var cmdPids = GetCmdExePids();
        var results = new List<CmdWindow>();

        User32.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (!User32.IsWindowVisible(hwnd)) return true;

            _ = User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (!cmdPids.Contains(pid)) return true;

            var title = GetWindowTitle(hwnd);
            var cwd = TryGetProcessCwd(pid);

            results.Add(new CmdWindow(pid, hwnd, title, cwd));
            return true;
        };

        User32.EnumWindows(callback, IntPtr.Zero);
        return results;
    }

    static HashSet<uint> GetCmdExePids()
    {
        var pids = new HashSet<uint>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name = 'cmd.exe'");
            foreach (ManagementObject obj in searcher.Get())
            {
                pids.Add(Convert.ToUInt32(obj["ProcessId"]));
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"GetCmdExePids failed: {exception.Message}");
        }
        return pids;
    }

    static string GetWindowTitle(IntPtr hwnd)
    {
        var length = User32.GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        var copied = User32.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, copied);
    }

    static unsafe string? TryGetProcessCwd(uint pid)
    {
        var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION | Kernel32.PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var pbi = default(ProcessBasicInformation);
            var status = NtDll.NtQueryInformationProcess(handle, NtDll.PROCESSBASICINFORMATION, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return null;

            // Offsets for 64-bit PEB:
            //   PEB + 0x20  → ProcessParameters (PTR)
            //   ProcessParameters + 0x38 → CurrentDirectory.DosPath (UNICODE_STRING)
            //   UNICODE_STRING: USHORT Length; USHORT MaximumLength; PWSTR Buffer;
            //     Length at +0x00, Buffer at +0x08 (64-bit).

            IntPtr processParameters;
            if (!Kernel32.ReadProcessMemory(handle, pbi.PebBaseAddress + 0x20, &processParameters, (nuint)IntPtr.Size, out _))
                return null;

            ushort length;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x38, &length, sizeof(ushort), out _))
                return null;
            if (length == 0 || length > 4096) return null;

            IntPtr bufferPtr;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x38 + 8, &bufferPtr, (nuint)IntPtr.Size, out _))
                return null;
            if (bufferPtr == IntPtr.Zero) return null;

            var chars = new char[length / 2];
            fixed (char* dest = chars)
            {
                if (!Kernel32.ReadProcessMemory(handle, bufferPtr, dest, length, out _))
                    return null;
            }
            return new string(chars).TrimEnd('\\');
        }
        catch (Exception exception)
        {
            Logger.Log($"TryGetProcessCwd({pid}) failed: {exception.Message}");
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }
}
```

> **PEB offsets note:** These offsets are for 64-bit Windows processes. MegaSchoen and ClaudeHookBridge must be built x64 (the solution already enforces this). If a future Windows update changes offsets, the impact is localized to `TryGetProcessCwd`.

- [x] **Step 2: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 3: Commit**

(Manual verification of the PEB pipeline happens in Task 18 once the `resolve` subcommand is wired in.)

```bash
git add ClaudeCycler.Core/ProcessResolver.cs
git commit -m "Add ProcessResolver with cmd.exe enumeration and PEB-based cwd read"
```

---

## Task 18: Resolve subcommand

**Files:**
- Create: `ClaudeHookBridge/Commands/ResolveCommand.cs`
- Modify: `ClaudeHookBridge/Program.cs`

- [x] **Step 1: Implement**

`ClaudeHookBridge/Commands/ResolveCommand.cs`:

```csharp
using ClaudeCycler.Core;

namespace ClaudeHookBridge.Commands;

public static class ResolveCommand
{
    public static int Run()
    {
        var store = new StateStore();
        var file = store.ReadFresh(TimeSpan.FromMinutes(30));

        Console.WriteLine($"Fresh sessions: {file.Sessions.Count}");
        var windows = ProcessResolver.EnumerateCmdExeWindows();
        Console.WriteLine($"cmd.exe windows: {windows.Count}");
        Console.WriteLine();

        foreach (var (id, entry) in file.Sessions)
        {
            Console.WriteLine($"Session {id}");
            Console.WriteLine($"  cwd: {entry.Cwd}");

            var matches = windows.Where(w => CwdMatches(w.WorkingDirectory, entry.Cwd)).ToList();
            if (matches.Count == 0)
            {
                Console.WriteLine("  -> NO MATCHING WINDOW");
            }
            else
            {
                foreach (var w in matches)
                {
                    Console.WriteLine($"  -> pid={w.ProcessId} hwnd=0x{w.WindowHandle:X} title=\"{w.WindowTitle}\"");
                }
            }
        }

        return 0;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Wire into Program.cs**

Add `"resolve" => Commands.ResolveCommand.Run(),` to the switch.

- [x] **Step 3: Manual verify**

Open two cmd.exe windows in distinct directories, run claude in one, trigger a permission prompt (or mock it by running the hook bridge manually with a crafted stdin JSON). Then run `resolve`. Expected: one session listed with one matching pid/hwnd.

- [x] **Step 4: Commit**

```bash
git add ClaudeHookBridge/Commands/ResolveCommand.cs ClaudeHookBridge/Program.cs
git commit -m "Add ClaudeHookBridge resolve subcommand"
```

---

## Task 19: Win32ForegroundHelper (focus-steal-safe SetForegroundWindow)

**Files:**
- Create: `MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs`
- Modify: `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs`

- [x] **Step 1: Extend Win32Interop**

Append to `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs` (inside the partial class):

```csharp
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;
```

- [x] **Step 2: Create helper**

`MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs`:

```csharp
using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

static class Win32ForegroundHelper
{
    public static void BringToFront(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero) return;

        ShowWindow(targetHwnd, SW_RESTORE);

        var currentThread = GetCurrentThreadId();
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThread = GetWindowThreadProcessId(targetHwnd, out var targetPid);

        if (currentThread != foregroundThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }
        AllowSetForegroundWindow(targetPid);
        SetForegroundWindow(targetHwnd);
        if (currentThread != foregroundThread)
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
```

- [x] **Step 3: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 4: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/Win32Interop.cs MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs
git commit -m "Add Win32ForegroundHelper for focus-steal-safe activation"
```

---

## Task 20: Add named-hotkey support to GlobalHotkeyService

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/GlobalHotkeyService.cs`

- [x] **Step 1: Add named-hotkey fields, method, and event**

Modify `GlobalHotkeyService.cs`:

Add fields alongside `_hotkeyToProfile`:

```csharp
    readonly Dictionary<int, string> _hotkeyToName = new();
```

Add event alongside `HotkeyTriggered`:

```csharp
    public event EventHandler<string>? NamedHotkeyTriggered;
```

Add method:

```csharp
    public bool RegisterNamedHotkey(string name, string key, IEnumerable<string> modifiers)
    {
        var vk = KeyToVirtualKey(key);
        if (vk == 0)
        {
            System.Diagnostics.Debug.WriteLine($"Unknown key: {key}");
            return false;
        }

        uint modifiersMask = MOD_NOREPEAT;
        foreach (var mod in modifiers)
        {
            modifiersMask |= ModifierToFlag(mod);
        }

        var hotkeyId = _nextHotkeyId++;
        var success = RegisterHotKey(_messageWindow.Handle, hotkeyId, modifiersMask, vk);

        if (success)
        {
            _hotkeyToName[hotkeyId] = name;
            System.Diagnostics.Debug.WriteLine($"Registered named hotkey {hotkeyId} for \"{name}\"");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register named hotkey \"{name}\": error {GetLastError()}");
        }

        return success;
    }
```

Modify `OnHotkeyPressed` to also route named hotkeys:

```csharp
    void OnHotkeyPressed(object? sender, int hotkeyId)
    {
        if (_hotkeyToProfile.TryGetValue(hotkeyId, out var profileId))
        {
            System.Diagnostics.Debug.WriteLine($"Hotkey {hotkeyId} pressed, triggering profile {profileId}");
            HotkeyTriggered?.Invoke(this, profileId);
            return;
        }

        if (_hotkeyToName.TryGetValue(hotkeyId, out var name))
        {
            System.Diagnostics.Debug.WriteLine($"Hotkey {hotkeyId} pressed, triggering named hotkey \"{name}\"");
            NamedHotkeyTriggered?.Invoke(this, name);
        }
    }
```

Modify `UnregisterAll` to include named hotkeys:

```csharp
    public void UnregisterAll()
    {
        foreach (var hotkeyId in _hotkeyToProfile.Keys.Concat(_hotkeyToName.Keys).ToList())
        {
            UnregisterHotKey(_messageWindow.Handle, hotkeyId);
        }
        _hotkeyToProfile.Clear();
        _hotkeyToName.Clear();
    }
```

- [x] **Step 2: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Expected: build succeeds, existing behavior untouched.

- [x] **Step 3: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/GlobalHotkeyService.cs
git commit -m "Add named-hotkey support to GlobalHotkeyService"
```

---

## Task 21: ClaudeWindowService

**Files:**
- Create: `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`
- Modify: `MegaSchoen/MegaSchoen.csproj`

- [x] **Step 1: Add ClaudeCycler.Core project reference**

In `MegaSchoen/MegaSchoen.csproj`, add to the existing `<ItemGroup>` that holds ProjectReferences:

```xml
    <ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" />
```

- [x] **Step 2: Implement ClaudeWindowService**

`MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`:

```csharp
using ClaudeCycler.Core;

namespace MegaSchoen.Platforms.Windows.Services;

sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly StateStore _store = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray)
    {
        _tray = tray;
    }

    public void CycleToNext()
    {
        var file = _store.ReadFresh(TimeSpan.FromMinutes(30));
        if (file.Sessions.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
            return;
        }

        var windows = ProcessResolver.EnumerateCmdExeWindows();
        var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
        foreach (var (id, entry) in file.Sessions)
        {
            foreach (var window in windows)
            {
                if (CwdMatches(window.WorkingDirectory, entry.Cwd))
                {
                    candidates.Add((id, window, entry.NotifiedAt));
                }
            }
        }

        if (candidates.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "Waiting sessions couldn't be resolved to windows", NotificationIcon.Warning);
            return;
        }

        candidates.Sort((a, b) => a.NotifiedAt.CompareTo(b.NotifiedAt));

        var lastIndex = candidates.FindIndex(c => c.Window.WindowHandle == _lastFocused);
        var nextIndex = (lastIndex + 1) % candidates.Count;
        var next = candidates[nextIndex];

        Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
        _lastFocused = next.Window.WindowHandle;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 3: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 4: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs MegaSchoen/MegaSchoen.csproj
git commit -m "Add ClaudeWindowService cycling focus through needy Claude sessions"
```

---

## Task 22: Register service and wire hotkey

**Files:**
- Modify: `MegaSchoen/MauiProgram.cs`
- Modify: `MegaSchoen/Platforms/Windows/App.xaml.cs`

- [x] **Step 1: Register in DI**

In `MegaSchoen/MauiProgram.cs`, inside the `#if WINDOWS` block, add:

```csharp
        builder.Services.AddSingleton<ClaudeWindowService>();
```

- [x] **Step 2: Wire hotkey in App.xaml.cs**

In `MegaSchoen/Platforms/Windows/App.xaml.cs`, inside `InitializeWindowsServices()`:

Resolve the new service. Right after the line `var hotkeys = services.GetRequiredService<GlobalHotkeyService>();`, add:

```csharp
        var claudeWindowService = services.GetRequiredService<ClaudeWindowService>();
```

Register the hotkey and its handler. Directly after the existing `hotkeys.HotkeyTriggered += ...` block (the one that handles `profileId`), add:

```csharp
        hotkeys.RegisterNamedHotkey("claude-cycle", "C", new[] { "Control", "Alt", "Shift" });
        hotkeys.NamedHotkeyTriggered += (s, name) =>
        {
            if (name == "claude-cycle")
            {
                claudeWindowService.CycleToNext();
            }
        };
```

- [x] **Step 3: Build and run manually**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Launch MegaSchoen. Press `Ctrl+Alt+Shift+C`. Expected: tray notification "No Claude windows waiting" (since hooks aren't installed yet).

- [x] **Step 4: Commit**

```bash
git add MegaSchoen/MauiProgram.cs MegaSchoen/Platforms/Windows/App.xaml.cs
git commit -m "Wire Ctrl+Alt+Shift+C to ClaudeWindowService.CycleToNext"
```

---

## Task 23: Tray menu item for installing Claude hooks

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs`
- Modify: `MegaSchoen/Platforms/Windows/App.xaml.cs`

- [x] **Step 1: Add menu ID and event**

In `TrayIconService.cs`, add a new menu ID constant alongside `MenuIdOpen`:

```csharp
    const int MenuIdInstallClaudeHooks = 1002;
```

Add an event alongside `ShowRequested`:

```csharp
    public event EventHandler? InstallClaudeHooksRequested;
```

- [x] **Step 2: Insert menu item**

In `ShowContextMenu`, after the `InsertMenu(... MenuIdOpen, "Open MegaSchoen")` line and before the separator, add:

```csharp
            InsertMenu(hMenu, position++, MF_STRING, MenuIdInstallClaudeHooks, "Install Claude Hooks");
```

- [x] **Step 3: Handle the command**

In `HandleMenuCommand`, add:

```csharp
        else if (cmd == MenuIdInstallClaudeHooks)
        {
            InstallClaudeHooksRequested?.Invoke(this, EventArgs.Empty);
        }
```

- [x] **Step 4: Wire handler in App.xaml.cs**

In `InitializeWindowsServices()`, after the existing `tray.ExitRequested += ...` block, add:

```csharp
        tray.InstallClaudeHooksRequested += (s, e) =>
        {
            try
            {
                var bridgePath = Path.Combine(AppContext.BaseDirectory, "ClaudeHookBridge.exe");
                var installer = new ClaudeCycler.Core.SettingsJsonInstaller();
                installer.Install(bridgePath);
                tray.ShowNotification("MegaSchoen", "Claude hooks installed");
            }
            catch (Exception exception)
            {
                tray.ShowNotification("MegaSchoen", $"Install failed: {exception.Message}", NotificationIcon.Error);
            }
        };
```

- [x] **Step 5: Build**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`

- [x] **Step 6: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/TrayIconService.cs MegaSchoen/Platforms/Windows/App.xaml.cs
git commit -m "Add tray menu item to install Claude hooks"
```

---

## Task 24: Post-build copy ClaudeHookBridge next to MegaSchoen.exe

**Files:**
- Modify: `MegaSchoen/MegaSchoen.csproj`

- [x] **Step 1: Add MSBuild target**

After the existing `CopyNativeDllToOutput` target in `MegaSchoen.csproj`, add:

```xml
	<Target Name="CopyClaudeHookBridge" AfterTargets="Build" Condition="$(TargetFramework.Contains('windows'))">
		<ItemGroup>
			<ClaudeHookBridgeFiles Include="..\ClaudeHookBridge\bin\$(Configuration)\net10.0-windows10.0.26100.0\*.*" />
		</ItemGroup>
		<Copy SourceFiles="@(ClaudeHookBridgeFiles)" DestinationFolder="$(OutputPath)" SkipUnchangedFiles="true" />
	</Target>
```

- [x] **Step 2: Verify**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64`
Expected: `ClaudeHookBridge.exe` appears in MegaSchoen's output directory.

- [x] **Step 3: Commit**

```bash
git add MegaSchoen/MegaSchoen.csproj
git commit -m "Copy ClaudeHookBridge to MegaSchoen output directory"
```

---

## Task 25: Manual smoke test

**Files:** (no code changes)

- [ ] **Step 1: Install hooks**

Launch MegaSchoen. Right-click the tray icon → Install Claude Hooks. Expect: notification "Claude hooks installed" and `~/.claude/settings.json.bak` exists.

- [ ] **Step 2: Verify with `check`**

Run: `<MegaSchoen output>\ClaudeHookBridge.exe check`
Expect: all three events reported INSTALLED (this binary), exit code 0.

- [ ] **Step 3: Three Claude sessions, three directories**

Open three separate cmd.exe windows, `cd` to three distinct directories, start `claude` in each.

- [ ] **Step 4: Trigger a permission prompt in one session**

Ask the Claude in one window to do something requiring approval (e.g., run a shell command). While the permission prompt is showing:

Run: `ClaudeHookBridge.exe status`
Expect: one entry with that session's cwd.

Run: `ClaudeHookBridge.exe resolve`
Expect: one session resolved to one cmd.exe pid/hwnd with the matching title.

- [ ] **Step 5: Hotkey focuses the right window**

Alt-tab to another window. Press `Ctrl+Alt+Shift+C`. Expect: the needy cmd.exe window comes forward.

- [ ] **Step 6: Approve the prompt**

Press Enter to approve. Claude begins executing; when it finishes, `Stop` fires.

Run: `ClaudeHookBridge.exe status`
Expect: no entries.

Press hotkey → expect "No Claude windows waiting" tray notification.

- [ ] **Step 7: Two concurrent needy sessions**

Trigger permission prompts in two sessions concurrently (or in quick succession). Press hotkey repeatedly. Expect: focus cycles deterministically through both, oldest first.

- [ ] **Step 8: Kill a Claude session mid-wait**

While a session is waiting, close its cmd.exe. Press hotkey → expect: skipped cleanly (tray says nothing waiting if only one was needy, or cycles to the other).

- [ ] **Step 9: Stale-entry cutoff**

If a session remains in state file > 30 min without being cleared (edge case), `status` should flag it `[STALE]` and `CycleToNext` should ignore it.

- [ ] **Step 10: Orphaned entry in resolve**

Edit the state file by hand to include a session whose cwd matches no running cmd.exe. Run `resolve`. Expect: "NO MATCHING WINDOW" line. Hotkey press: tray notification that sessions can't be resolved.

If any step fails, create an issue or go back to the relevant task; don't claim done until all ten pass.

---

## Testing summary

- **Unit tests** (all in `ClaudeCycler.Core.Tests`): Paths, StateStore (read/write/upsert/delete/stale), HookDispatcher (all event types), SettingsJsonInstaller (install/backup/idempotent/status).
- **Integration test**: `HookModeIntegrationTests` invokes the real `ClaudeHookBridge.exe` with crafted stdin.
- **Manual smoke test** (Task 25): end-to-end with real Claude sessions.

Run all tests: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`

---

## Deferred to follow-up (per spec)

- Dedicated GUI tab in MegaSchoen showing live state, hook install status, per-session focus buttons, hotkey binding UI.
- User-configurable hotkey (currently hardcoded to `Ctrl+Alt+Shift+C`).
- PowerShell / Windows Terminal / WSL terminal support.
- `logs -f` tail mode.
- Cross-platform window focusing (macOS/Linux).
