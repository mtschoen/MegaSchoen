# Claude Cycler — Any-Input Cycle & Verifier Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [docs/superpowers/specs/2026-05-06-claude-cycler-any-input-design.md](../specs/2026-05-06-claude-cycler-any-input-design.md)

**Goal:** Add a `Ctrl+Alt+0` cycle (and matching tray/UI buttons) for every Claude session waiting on any input, parallel to the existing `Ctrl+Alt+9` permissions-only cycle, while fixing the multi-permission deletion bug and hardening `SessionLivenessVerifier` against transcript-touched-without-progress false evictions.

**Architecture:** Extend `SessionEntry` with a `WaitingReason` (`Permission` | `AwaitingInput`). Rewire `HookDispatcher` so `Stop` upserts as AwaitingInput (instead of deleting) and `PostToolUse` is dropped. Replace the verifier's modtime-only check with a two-tier scheme: fast-path on modtime, slow-path tail-reads the transcript JSONL and classifies the last entry by `type`. `ClaudeWindowService.CycleToNext` takes an optional `WaitingReason?` filter. `App.xaml.cs` registers two named hotkeys (`9` for perms-only, `0` for any) and `TrayIconService` exposes parallel menu items.

**Tech Stack:** .NET 10 / C# 13, MSTest (`[TestClass]`/`[TestMethod]`), MAUI (Windows-only WinUI shell), Win32 `RegisterHotKey`. Build with MSBuild (per `CLAUDE.md`); tests run via `dotnet test` against the managed-only `ClaudeCycler.Core.Tests` project.

**Test commands:**
- Build the affected library: `MSBuild.exe ClaudeCycler.Core/ClaudeCycler.Core.csproj -p:Configuration=Debug -nodeReuse:false`
- Build + run unit tests: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj`
- Build the full app for smoke test: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false`
- Launch the app: `MegaSchoen\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MegaSchoen.exe`

> **Pre-flight:** before starting Task 1, kill any running `MegaSchoen.exe` (`Get-Process MegaSchoen -ErrorAction SilentlyContinue | Stop-Process`) — `SingleInstanceService` will otherwise silently swallow new launches and you'll smoke-test stale builds (per `project_single_instance_test_trap.md`).

**File map (what each task touches):**

| File | Purpose |
|---|---|
| `ClaudeCycler.Core/Models/WaitingReason.cs` | NEW — enum |
| `ClaudeCycler.Core/Models/SessionEntry.cs` | Add `Reason` property |
| `ClaudeCycler.Core/HookDispatcher.cs` | `Stop` upserts AwaitingInput; drop `PostToolUse`; tag permission upserts with reason |
| `ClaudeCycler.Core/SessionLivenessVerifier.cs` | Add tail-read JSONL classifier as slow path |
| `ClaudeCycler.Core.Tests/HookDispatcherTests.cs` | Update + add tests for new dispatcher behavior |
| `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs` | Add tests for the classifier slow path |
| `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs` | `CycleToNext(WaitingReason?)` |
| `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs` | Split menu, rename event, add new event/menu id |
| `MegaSchoen/Platforms/Windows/App.xaml.cs` | Register two named hotkeys; dispatch by name; wire two tray events |
| `MegaSchoen/MainPage.xaml` | Add second debug button |
| `MegaSchoen/MainPage.xaml.cs` | Add second click handler |

---

### Task 1: Add `WaitingReason` enum and `Reason` field to `SessionEntry`

**Files:**
- Create: `ClaudeCycler.Core/Models/WaitingReason.cs`
- Modify: `ClaudeCycler.Core/Models/SessionEntry.cs`
- Test: `ClaudeCycler.Core.Tests/StateStoreTests.cs` (add roundtrip test)

- [ ] **Step 1: Write the failing roundtrip test**

Append to `ClaudeCycler.Core.Tests/StateStoreTests.cs` inside the existing `[TestClass]` body. Add two tests — one covering explicit Reason persistence, one covering legacy entries with no Reason field deserializing to `Permission` (the enum default = first member = 0):

```csharp
[TestMethod]
public void Reason_RoundtripsThroughStore()
{
    var path = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
    try
    {
        var store = new StateStore(path);
        store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        var roundtripped = new StateStore(path).Read();
        Assert.AreEqual(WaitingReason.AwaitingInput, roundtripped.Sessions["s1"].Reason);
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}

[TestMethod]
public void Reason_LegacyEntryWithoutField_DefaultsToPermission()
{
    var path = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
    try
    {
        // Simulate a state file written by the previous version (no "reason" field).
        File.WriteAllText(path, """
            {
              "version": 1,
              "sessions": {
                "s1": {
                  "cwd": "C:\\foo",
                  "transcriptPath": null,
                  "notifiedAt": "2026-04-01T00:00:00+00:00",
                  "message": "old"
                }
              }
            }
            """);

        var loaded = new StateStore(path).Read();
        Assert.AreEqual(WaitingReason.Permission, loaded.Sessions["s1"].Reason);
    }
    finally
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
```

- [ ] **Step 2: Run tests; confirm they fail to compile**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~Reason_"`
Expected: build error — `WaitingReason` not defined, `SessionEntry.Reason` not defined.

- [ ] **Step 3: Create the enum**

Create `ClaudeCycler.Core/Models/WaitingReason.cs`:

```csharp
namespace ClaudeCycler.Core.Models;

public enum WaitingReason
{
    Permission,
    AwaitingInput
}
```

- [ ] **Step 4: Add the field to `SessionEntry`**

Modify `ClaudeCycler.Core/Models/SessionEntry.cs` to add a single property after `Message`:

```csharp
using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class SessionEntry
{
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("notifiedAt")]
    public DateTimeOffset NotifiedAt { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("reason")]
    public WaitingReason Reason { get; set; } = WaitingReason.Permission;
}
```

The `= WaitingReason.Permission` default makes the legacy-deserialization test pass: when the JSON has no `reason` key, `System.Text.Json` leaves the property at its initialized default.

- [ ] **Step 5: Run tests; confirm they pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~Reason_"`
Expected: 2 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add ClaudeCycler.Core/Models/WaitingReason.cs ClaudeCycler.Core/Models/SessionEntry.cs ClaudeCycler.Core.Tests/StateStoreTests.cs
git commit -m "feat(cycler): add WaitingReason on SessionEntry"
```

---

### Task 2: `HookDispatcher` tags permission upserts with `Reason=Permission`

**Files:**
- Modify: `ClaudeCycler.Core/HookDispatcher.cs`
- Test: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Strengthen the existing permission test**

In `HookDispatcherTests.cs`, append to `Notification_PermissionPrompt_UpsertsSession` an assertion that the upserted entry has `Reason == Permission`:

```csharp
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
    Assert.AreEqual(WaitingReason.Permission, file.Sessions["s1"].Reason);
}
```

- [ ] **Step 2: Run; confirm it passes already**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~Notification_PermissionPrompt_UpsertsSession"`
Expected: PASS — because `Reason`'s default is `Permission`, the existing dispatcher code already produces the correct value.

> **Why include this task at all?** Locks in the contract for future readers and prevents a regression if someone changes the default later.

- [ ] **Step 3: Make the dispatcher set Reason explicitly (defensive)**

Modify `ClaudeCycler.Core/HookDispatcher.cs`'s `Notification` case so it sets `Reason` explicitly rather than relying on the default:

```csharp
case "Notification" when payload.NotificationType == "permission_prompt":
    _store.Upsert(payload.SessionId, new SessionEntry
    {
        Cwd = payload.Cwd ?? "",
        TranscriptPath = payload.TranscriptPath,
        NotifiedAt = DateTimeOffset.UtcNow,
        Message = payload.Message,
        Reason = WaitingReason.Permission
    });
    break;
```

Add `using ClaudeCycler.Core.Models;` if not already present (it is — already used by the file).

- [ ] **Step 4: Run; confirm still passes**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~HookDispatcherTests"`
Expected: all current `HookDispatcherTests` PASS (this task did not change any other behavior).

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "refactor(cycler): tag permission upserts with explicit Reason"
```

---

### Task 3: `Stop` upserts as `AwaitingInput` (was: delete)

**Files:**
- Modify: `ClaudeCycler.Core/HookDispatcher.cs`
- Test: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Replace the existing `Stop_DeletesSession` test**

In `HookDispatcherTests.cs` find this test:

```csharp
[TestMethod]
public void Stop_DeletesSession()
{
    _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
    _dispatcher.Dispatch(new HookPayload { HookEventName = "Stop", SessionId = "s1" });

    Assert.IsEmpty(_store.Read().Sessions);
}
```

Replace it with three tests covering the new behavior:

```csharp
[TestMethod]
public void Stop_UpsertsSessionAsAwaitingInput()
{
    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Stop",
        SessionId = "s1",
        Cwd = "C:\\foo",
        TranscriptPath = "C:\\foo\\transcript.jsonl"
    });

    var file = _store.Read();
    Assert.IsTrue(file.Sessions.ContainsKey("s1"));
    Assert.AreEqual(WaitingReason.AwaitingInput, file.Sessions["s1"].Reason);
    Assert.AreEqual("C:\\foo", file.Sessions["s1"].Cwd);
    Assert.AreEqual("C:\\foo\\transcript.jsonl", file.Sessions["s1"].TranscriptPath);
}

[TestMethod]
public void Stop_AfterPermissionPrompt_OverwritesReason()
{
    _store.Upsert("s1", new SessionEntry
    {
        Cwd = "C:\\foo",
        NotifiedAt = DateTimeOffset.UtcNow,
        Reason = WaitingReason.Permission
    });

    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Stop",
        SessionId = "s1",
        Cwd = "C:\\foo"
    });

    Assert.AreEqual(WaitingReason.AwaitingInput, _store.Read().Sessions["s1"].Reason);
}

[TestMethod]
public void Stop_RefreshesNotifiedAt()
{
    var earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
    _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = earlier });

    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Stop",
        SessionId = "s1",
        Cwd = "C:\\foo"
    });

    Assert.IsTrue(_store.Read().Sessions["s1"].NotifiedAt > earlier);
}
```

- [ ] **Step 2: Run tests; confirm they fail**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~Stop_"`
Expected: `Stop_UpsertsSessionAsAwaitingInput` FAILS (current dispatcher deletes, so `s1` is absent). The other two also fail.

- [ ] **Step 3: Change the dispatcher**

Modify `ClaudeCycler.Core/HookDispatcher.cs`. Remove `Stop` from the delete-case list and add a dedicated `Stop` case that mirrors the permission upsert but with `AwaitingInput`:

```csharp
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
                    TranscriptPath = payload.TranscriptPath,
                    NotifiedAt = DateTimeOffset.UtcNow,
                    Message = payload.Message,
                    Reason = WaitingReason.Permission
                });
                break;

            case "Stop":
                _store.Upsert(payload.SessionId, new SessionEntry
                {
                    Cwd = payload.Cwd ?? "",
                    TranscriptPath = payload.TranscriptPath,
                    NotifiedAt = DateTimeOffset.UtcNow,
                    Message = null,
                    Reason = WaitingReason.AwaitingInput
                });
                break;

            case "UserPromptSubmit":
            case "PostToolUse":
            case "SessionEnd":
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
```

(`PostToolUse` is still in the delete list at this step. Task 4 removes it.)

- [ ] **Step 4: Run; confirm tests pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~Stop_"`
Expected: 3 passed.

Also run the full dispatcher suite to catch regressions:
Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~HookDispatcherTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(cycler): Stop event upserts session as AwaitingInput"
```

---

### Task 4: Drop `PostToolUse` handler from dispatcher

**Files:**
- Modify: `ClaudeCycler.Core/HookDispatcher.cs`
- Test: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Replace the existing `PostToolUse_DeletesSession` test**

In `HookDispatcherTests.cs` find this test:

```csharp
[TestMethod]
public void PostToolUse_DeletesSession()
{
    _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
    _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1" });

    Assert.IsEmpty(_store.Read().Sessions);
}
```

Replace its body to assert the entry is **preserved**, and rename to match new semantics:

```csharp
[TestMethod]
public void PostToolUse_LeavesEntryIntact()
{
    var existing = new SessionEntry
    {
        Cwd = "C:\\foo",
        NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        Reason = WaitingReason.Permission
    };
    _store.Upsert("s1", existing);

    _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1" });

    var file = _store.Read();
    Assert.IsTrue(file.Sessions.ContainsKey("s1"));
    Assert.AreEqual(WaitingReason.Permission, file.Sessions["s1"].Reason);
    Assert.AreEqual(existing.NotifiedAt, file.Sessions["s1"].NotifiedAt);
}
```

The existing `PostToolUse_NoMatchingEntry_IsNoop` test stays valid (still no-ops because it remains in the default branch).

- [ ] **Step 2: Run; confirm it fails**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~PostToolUse_LeavesEntryIntact"`
Expected: FAIL — current dispatcher deletes `s1`, so `Sessions` is empty.

- [ ] **Step 3: Drop the case from the switch**

In `ClaudeCycler.Core/HookDispatcher.cs` remove `case "PostToolUse":` from the delete-case group. Result:

```csharp
case "UserPromptSubmit":
case "SessionEnd":
    _store.Delete(payload.SessionId);
    break;
```

`PostToolUse` now falls through to the `default` branch and is logged + ignored, which is the desired behavior.

- [ ] **Step 4: Run; confirm tests pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~HookDispatcherTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "fix(cycler): drop PostToolUse handler to fix multi-prompt loss"
```

---

### Task 5: `SessionLivenessVerifier` — tail-read JSONL slow path

**Files:**
- Modify: `ClaudeCycler.Core/SessionLivenessVerifier.cs`
- Test: `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs`

- [ ] **Step 1: Write the failing classifier tests**

Append to `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs`. These tests force the slow path by writing a transcript whose modtime is well past `NotifiedAt + grace`, so the modtime check evicts the entry under the current implementation but the new classifier should rescue it (or correctly resolve it):

```csharp
[TestMethod]
public void SlowPath_LastEntryIsAssistant_ReturnsTrue()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"user","timestamp":"2026-05-06T00:00:00Z","message":{"role":"user","content":"hi"}}""" + "\n" +
        """{"type":"assistant","timestamp":"2026-05-06T00:00:01Z","message":{"role":"assistant","content":"working..."}}""" + "\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)  // forces slow path
    };

    Assert.IsTrue(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_LastEntryIsUser_ReturnsFalse()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"done"}}""" + "\n" +
        """{"type":"user","timestamp":"2026-05-06T00:00:05Z","message":{"role":"user","content":"thanks"}}""" + "\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    Assert.IsFalse(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_LastEntryIsToolResult_ReturnsFalse()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"running tool"}}""" + "\n" +
        """{"type":"tool_result","timestamp":"2026-05-06T00:00:02Z","tool_use_id":"x"}""" + "\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    Assert.IsFalse(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_LastEntryIsSystem_ReturnsFalse()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n" +
        """{"type":"system","timestamp":"2026-05-06T00:00:30Z","content":"/model claude-opus-4-7"}""" + "\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    Assert.IsFalse(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_MalformedLastLine_ReturnsTrueFailSafe()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n" +
        "{not-json" + "\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    Assert.IsTrue(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_EmptyFile_ReturnsFalse()
{
    File.WriteAllText(_tempTranscript, "");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    // Modtime > threshold AND no positive pending signal → resolved.
    Assert.IsFalse(verifier.IsStillWaiting(entry));
}

[TestMethod]
public void SlowPath_TrailingBlankLines_ClassifiesLastNonEmpty()
{
    File.WriteAllText(_tempTranscript,
        """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n\n\n");

    var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
    var entry = new SessionEntry
    {
        TranscriptPath = _tempTranscript,
        NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    Assert.IsTrue(verifier.IsStillWaiting(entry));
}
```

- [ ] **Step 2: Run; confirm they fail**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~SlowPath_"`
Expected: most FAIL — the current implementation only does the modtime check and returns `false` whenever modtime > threshold, regardless of content.

- [ ] **Step 3: Replace the verifier with the two-tier implementation**

Replace `ClaudeCycler.Core/SessionLivenessVerifier.cs` entirely:

```csharp
using System.Text.Json;
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class SessionLivenessVerifier
{
    static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);
    const int TailReadChunkSize = 4096;
    const int TailReadMaxBytes = 256 * 1024;

    readonly TimeSpan _grace;

    public SessionLivenessVerifier(TimeSpan? grace = null)
    {
        _grace = grace ?? DefaultGrace;
    }

    public bool IsStillWaiting(SessionEntry entry)
    {
        if (string.IsNullOrEmpty(entry.TranscriptPath) || !File.Exists(entry.TranscriptPath))
        {
            return false;
        }

        var transcriptTouchedAt = File.GetLastWriteTimeUtc(entry.TranscriptPath);
        var threshold = entry.NotifiedAt.UtcDateTime + _grace;

        if (transcriptTouchedAt <= threshold)
        {
            return true;
        }

        return ClassifyLastEntry(entry.TranscriptPath) == LastEntryClass.SessionPending;
    }

    enum LastEntryClass
    {
        SessionPending,
        Resolved
    }

    static LastEntryClass ClassifyLastEntry(string transcriptPath)
    {
        string? lastLine;
        try
        {
            lastLine = ReadLastNonEmptyLine(transcriptPath);
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionLivenessVerifier: tail read failed for {transcriptPath}: {exception.Message}");
            return LastEntryClass.SessionPending; // fail-safe
        }

        if (string.IsNullOrWhiteSpace(lastLine))
        {
            return LastEntryClass.Resolved;
        }

        string? type;
        try
        {
            using var doc = JsonDocument.Parse(lastLine);
            type = doc.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return LastEntryClass.SessionPending; // fail-safe on malformed JSON
        }

        return type switch
        {
            "assistant" => LastEntryClass.SessionPending,
            "user"        => LastEntryClass.Resolved,
            "tool_result" => LastEntryClass.Resolved,
            "system"      => LastEntryClass.Resolved,
            _             => LastEntryClass.Resolved
        };
    }

    static string? ReadLastNonEmptyLine(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length == 0)
        {
            return null;
        }

        var totalRead = 0;
        var buffer = new List<byte>(capacity: TailReadChunkSize);

        while (totalRead < stream.Length && totalRead < TailReadMaxBytes)
        {
            var chunkSize = (int)Math.Min(TailReadChunkSize, stream.Length - totalRead);
            stream.Seek(-(totalRead + chunkSize), SeekOrigin.End);

            var chunk = new byte[chunkSize];
            var bytesRead = stream.Read(chunk, 0, chunkSize);
            buffer.InsertRange(0, chunk.AsSpan(0, bytesRead).ToArray());
            totalRead += bytesRead;

            // We need at least one newline followed by some content to identify the last non-empty line.
            // Strip trailing whitespace, then look for the last newline before that content.
            var trimmedEnd = buffer.Count;
            while (trimmedEnd > 0 && (buffer[trimmedEnd - 1] == (byte)'\n' || buffer[trimmedEnd - 1] == (byte)'\r'))
            {
                trimmedEnd--;
            }

            if (trimmedEnd == 0)
            {
                // entire buffer is whitespace; need to keep reading earlier in the file
                if (totalRead >= stream.Length) return null;
                continue;
            }

            // Find the last newline before the trimmed content.
            var newlineIndex = -1;
            for (var i = trimmedEnd - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    newlineIndex = i;
                    break;
                }
            }

            if (newlineIndex >= 0)
            {
                var lineBytes = buffer.GetRange(newlineIndex + 1, trimmedEnd - newlineIndex - 1);
                return System.Text.Encoding.UTF8.GetString(lineBytes.ToArray());
            }

            // No newline found in this chunk; the line might extend further back. Loop.
            if (totalRead >= stream.Length)
            {
                // Whole file is one line.
                return System.Text.Encoding.UTF8.GetString(buffer.GetRange(0, trimmedEnd).ToArray());
            }
        }

        // Hit the cap. Treat as "couldn't classify" → caller's fail-safe returns SessionPending.
        throw new IOException($"Transcript tail exceeded {TailReadMaxBytes} bytes without finding a line boundary");
    }
}
```

- [ ] **Step 4: Run; confirm all tests pass**

Run: `dotnet test ClaudeCycler.Core.Tests/ClaudeCycler.Core.Tests.csproj --filter "FullyQualifiedName~SessionLivenessVerifierTests"`
Expected: all PASS — both the original 5 tests (fast path) and the 7 new slow-path tests.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/SessionLivenessVerifier.cs ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs
git commit -m "feat(cycler): tail-read JSONL classifier as verifier slow path"
```

---

### Task 6: `ClaudeWindowService.CycleToNext` takes optional reason filter

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`

(No unit-test infrastructure exists for `ClaudeWindowService` — it depends on Win32 process enumeration. This task is verified via build + manual smoke test in Task 10.)

- [ ] **Step 1: Add the filter parameter**

In `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`, change the `CycleToNext` signature. Window matching and orphan-tracking still happen for *every* live entry (regardless of filter); the filter only controls which matched entries become cycle candidates. The complete updated method body:

```csharp
public void CycleToNext(WaitingReason? filter = null)
{
    var file = _store.Read();
    if (file.Sessions.Count == 0)
    {
        _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
        return;
    }

    var windows = ProcessResolver.EnumerateCmdExeWindows();
    var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
    var matchedSessionIds = new HashSet<string>();
    foreach (var (id, entry) in file.Sessions)
    {
        if (!_verifier.IsStillWaiting(entry))
        {
            _store.Delete(id);
            continue;
        }

        var includeInCycle = filter is null || entry.Reason == filter;

        foreach (var window in windows)
        {
            if (CwdMatches(window.WorkingDirectory, entry.Cwd))
            {
                matchedSessionIds.Add(id);
                if (includeInCycle)
                {
                    candidates.Add((id, window, entry.NotifiedAt));
                }
            }
        }
    }

    foreach (var id in file.Sessions.Keys)
    {
        if (!matchedSessionIds.Contains(id))
        {
            _store.Delete(id);
        }
    }

    if (candidates.Count == 0)
    {
        _tray.ShowNotification("MegaSchoen", "No live Claude windows waiting", NotificationIcon.Info);
        return;
    }

    candidates.Sort((a, b) => a.NotifiedAt.CompareTo(b.NotifiedAt));

    var lastIndex = candidates.FindIndex(c => c.Window.WindowHandle == _lastFocused);
    var nextIndex = (lastIndex + 1) % candidates.Count;
    var next = candidates[nextIndex];

    Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
    _lastFocused = next.Window.WindowHandle;
}
```

Add `using ClaudeCycler.Core.Models;` at the top if not already present (`WaitingReason` lives in that namespace; the file already has `using ClaudeCycler.Core;`).

> **Filter vs orphan cleanup — why split them.** The orphan cleanup wants to delete entries whose cmd.exe is gone; the filter wants to skip entries that don't match the current mode. Combining them would either (a) delete AwaitingInput entries when the user hits `Ctrl+Alt+9`, or (b) leak Permission entries with dead cmd.exe windows when the user only ever uses `Ctrl+Alt+0`. Splitting them: window-match always tracks `matchedSessionIds` (so orphan cleanup is independent of mode); only `candidates.Add` respects the filter.

- [ ] **Step 2: Build the project**

Run: `MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug -nodeReuse:false`
Expected: build SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs
git commit -m "feat(cycler): CycleToNext accepts optional WaitingReason filter"
```

---

### Task 7: `TrayIconService` — split menu, rename event, add new event/menu id

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs`

- [ ] **Step 1: Add new menu id constant + event**

In `TrayIconService.cs` near the existing `MenuIdCycleClaude = 1003` constant, add:

```csharp
const int MenuIdCycleAnyWaiting = 1005;
```

Rename the existing event `CycleClaudeRequested` to `CyclePermissionsRequested`, and add a new event:

```csharp
public event EventHandler? CyclePermissionsRequested;
public event EventHandler? CycleAnyWaitingRequested;
```

(Remove the old `CycleClaudeRequested` event declaration.)

- [ ] **Step 2: Update menu construction**

In `ShowContextMenu`, replace the single "Cycle Claude Now" line with two:

```csharp
InsertMenu(hMenu, position++, MF_STRING, MenuIdCycleClaude, "Cycle Pending Permissions");
InsertMenu(hMenu, position++, MF_STRING, MenuIdCycleAnyWaiting, "Cycle Any Waiting");
```

- [ ] **Step 3: Update menu command handler**

In `HandleMenuCommand`, replace the existing `MenuIdCycleClaude` branch with two:

```csharp
else if (cmd == MenuIdCycleClaude)
{
    CyclePermissionsRequested?.Invoke(this, EventArgs.Empty);
}
else if (cmd == MenuIdCycleAnyWaiting)
{
    CycleAnyWaitingRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 4: Build the project**

Run: `MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug -nodeReuse:false`
Expected: build FAILS at `App.xaml.cs` because it still references `CycleClaudeRequested`. That's exactly what Task 8 fixes.

- [ ] **Step 5: Stage but do not commit yet**

```bash
git add MegaSchoen/Platforms/Windows/Services/TrayIconService.cs
```

Commit happens at end of Task 8 (the rename leaves the build broken until App.xaml.cs is updated).

---

### Task 8: `App.xaml.cs` — register both hotkeys, wire two tray events

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/App.xaml.cs`

- [ ] **Step 1: Replace the tray cycle wiring**

In `App.xaml.cs` find the `tray.CycleClaudeRequested += …` block. Replace with two parallel handlers:

```csharp
tray.CyclePermissionsRequested += (s, e) =>
{
    try
    {
        claudeWindowService.CycleToNext(WaitingReason.Permission);
    }
    catch (Exception exception)
    {
        ClaudeCycler.Core.Logger.Log($"CyclePermissionsRequested threw: {exception}");
        tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
    }
};

tray.CycleAnyWaitingRequested += (s, e) =>
{
    try
    {
        claudeWindowService.CycleToNext(filter: null);
    }
    catch (Exception exception)
    {
        ClaudeCycler.Core.Logger.Log($"CycleAnyWaitingRequested threw: {exception}");
        tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
    }
};
```

Add `using ClaudeCycler.Core.Models;` if not already present.

- [ ] **Step 2: Replace the named hotkey registration and dispatcher**

Find the existing two lines:

```csharp
hotkeys.RegisterNamedHotkey("claude-cycle", "9", new[] { "Control", "Alt" });
hotkeys.NamedHotkeyTriggered += (s, name) => { ... };
```

Replace with:

```csharp
hotkeys.RegisterNamedHotkey("claude-cycle-perms", "9", new[] { "Control", "Alt" });
hotkeys.RegisterNamedHotkey("claude-cycle-any",   "0", new[] { "Control", "Alt" });

hotkeys.NamedHotkeyTriggered += (s, name) =>
{
    WaitingReason? filter = name switch
    {
        "claude-cycle-perms" => WaitingReason.Permission,
        "claude-cycle-any"   => null,
        _                    => null
    };

    if (name is not ("claude-cycle-perms" or "claude-cycle-any"))
    {
        return;
    }

    try
    {
        claudeWindowService.CycleToNext(filter);
    }
    catch (Exception exception)
    {
        ClaudeCycler.Core.Logger.Log($"CycleToNext threw: {exception}");
        tray.ShowNotification("MegaSchoen", $"Cycle failed: {exception.Message}", NotificationIcon.Error);
    }
};
```

- [ ] **Step 3: Verify cycler-namespace import**

Confirm `App.xaml.cs` already has the necessary imports (it imports `MegaSchoen.Platforms.Windows.Services` already; `WaitingReason` requires `using ClaudeCycler.Core.Models;`).

- [ ] **Step 4: Build the whole solution**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false`
Expected: build SUCCEEDED with no errors. (This is the first whole-solution build since Task 7 broke compilation.)

- [ ] **Step 5: Commit Task 7 + Task 8 together**

```bash
git add MegaSchoen/Platforms/Windows/App.xaml.cs
git commit -m "feat(cycler): split tray menu and hotkeys for perms vs any-waiting"
```

(Task 7's staged changes get included.)

---

### Task 9: `MainPage.xaml` second debug button + handler

**Files:**
- Modify: `MegaSchoen/MainPage.xaml`
- Modify: `MegaSchoen/MainPage.xaml.cs`

- [ ] **Step 1: Update the XAML debug section**

In `MegaSchoen/MainPage.xaml`, find the "Claude Cycler (debug)" frame block (around lines 217–234). Replace its inner `VerticalStackLayout` body so it has two buttons sharing one status label:

```xml
<VerticalStackLayout Spacing="10">
    <Label Text="🤖 Claude Cycler (debug)"
           FontSize="20"
           FontAttributes="Bold"/>
    <Button x:Name="CyclePermsButton"
            Text="Cycle Pending Permissions"
            Clicked="OnCyclePermsClicked"
            BackgroundColor="{StaticResource Primary}"/>
    <Button x:Name="CycleAnyWaitingButton"
            Text="Cycle Any Waiting"
            Clicked="OnCycleAnyWaitingClicked"
            BackgroundColor="{StaticResource Primary}"/>
    <Label x:Name="CycleClaudeStatusLabel"
           Text=""
           FontSize="12"
           TextColor="{StaticResource Gray600}"/>
</VerticalStackLayout>
```

(The old single button `CycleClaudeNowButton` with handler `OnCycleClaudeNowClicked` is replaced.)

- [ ] **Step 2: Replace the code-behind handler**

In `MegaSchoen/MainPage.xaml.cs`, replace the `OnCycleClaudeNowClicked` method with a parameterized helper plus two click handlers that delegate to it:

```csharp
#if WINDOWS
using ClaudeCycler.Core.Models;
using MegaSchoen.Platforms.Windows.Services;
#endif

namespace MegaSchoen
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        void OnCyclePermsClicked(object? sender, EventArgs eventArguments)
            => CycleClaude(filter: WaitingReason.Permission);

        void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments)
            => CycleClaude(filter: null);

        void CycleClaude(WaitingReason? filter)
        {
#if WINDOWS
            try
            {
                var services = Microsoft.UI.Xaml.Application.Current is MegaSchoen.WinUI.App
                    ? MauiWinUIApplication.Current.Services
                    : null;
                if (services is null)
                {
                    CycleClaudeStatusLabel.Text = "DI container not available";
                    return;
                }
                var cycler = services.GetService(typeof(ClaudeWindowService)) as ClaudeWindowService;
                if (cycler is null)
                {
                    CycleClaudeStatusLabel.Text = "ClaudeWindowService not resolved";
                    return;
                }
                cycler.CycleToNext(filter);
                var label = filter is null ? "any-waiting" : filter.ToString();
                CycleClaudeStatusLabel.Text = $"CycleToNext({label}) returned at {DateTimeOffset.UtcNow:O}";
            }
            catch (Exception exception)
            {
                CycleClaudeStatusLabel.Text = $"Threw: {exception.GetType().Name}: {exception.Message}";
                ClaudeCycler.Core.Logger.Log($"CycleClaude({filter}) threw: {exception}");
            }
#else
            CycleClaudeStatusLabel.Text = "Windows only";
#endif
        }
    }
}
```

- [ ] **Step 3: Build the whole solution**

Run: `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false`
Expected: build SUCCEEDED.

- [ ] **Step 4: Commit**

```bash
git add MegaSchoen/MainPage.xaml MegaSchoen/MainPage.xaml.cs
git commit -m "feat(cycler): MainPage debug buttons for perms and any-waiting cycles"
```

---

### Task 10: Manual smoke test

**Files:** none (verification only)

- [ ] **Step 1: Kill any running instance, rebuild from scratch**

PowerShell:
```powershell
Get-Process MegaSchoen -ErrorAction SilentlyContinue | Stop-Process -Force
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -t:Rebuild
```

Expected: build SUCCEEDED, no MegaSchoen.exe running.

- [ ] **Step 2: Launch the app**

```powershell
& 'MegaSchoen\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MegaSchoen.exe'
```

Expected: tray icon appears; main window opens.

- [ ] **Step 3: Verify tray menu**

Right-click the MegaSchoen tray icon. Expect to see two cycle items:
- "Cycle Pending Permissions"
- "Cycle Any Waiting"

The old single "Cycle Claude Now" should be gone.

- [ ] **Step 4: Verify debug buttons**

In the main window, scroll to the "Claude Cycler (debug)" section. Confirm both buttons render and clicking each updates the status label with `CycleToNext(Permission)` / `CycleToNext(any-waiting)`. With no Claude sessions running, the tray should show "No Claude windows waiting."

- [ ] **Step 5: Smoke-test with real Claude sessions**

Open three cmd.exe windows, each in a different cwd. In each, run `claude`. Then exercise:

1. In session A, ask Claude to do something that requires permission (`do `ls` for me` if Bash isn't allowed). When the prompt appears, do not answer.
2. In session B, send any prompt and let Claude finish (`hello`). Don't reply.
3. Press `Ctrl+Alt+9` — A should focus.
4. Press again — should report "No live Claude windows waiting" (only one perms entry).
5. Press `Ctrl+Alt+0` — A focuses. Press again — B focuses. Press again — wraps to A.
6. In session B, type `thanks` and submit. Press `Ctrl+Alt+0` — A focuses; second press reports no more.
7. In session A, approve the prompt. Hit `Ctrl+Alt+0` quickly — within ~5s the session is still in the cycle (verifier holds it via fast path); after Claude's `Stop` fires, it's cycled as AwaitingInput.
8. Multi-prompt test: in session C, ask Claude to run two parallel `Bash` calls. When both prompts queue, approve the first. Press `Ctrl+Alt+9` — C should still appear (verifier slow path keeps it via the unresolved `tool_use` in the transcript's last entry).

- [ ] **Step 6: Inspect state file**

```powershell
Get-Content "$env:APPDATA\MegaSchoen\needy-sessions.json"
```

Confirm entries have a `"reason"` field with values `"Permission"` or `"AwaitingInput"`.

- [ ] **Step 7: No commit**

This task adds no code; it's pure verification. If any check fails, revisit the failing task before declaring the plan complete.

---

## Self-review

**Spec coverage check:**

- ✅ `WaitingReason` enum + `SessionEntry.Reason` field — Task 1.
- ✅ Dispatcher: `Notification(permission_prompt)` → upsert with Permission — Task 2.
- ✅ Dispatcher: `Stop` → upsert with AwaitingInput — Task 3.
- ✅ Dispatcher: `PostToolUse` dropped — Task 4.
- ✅ Dispatcher: `UserPromptSubmit`, `SessionEnd` still delete — preserved in Tasks 3–4.
- ✅ Verifier: tail-read JSONL classifier — Task 5.
- ✅ Cycler: `CycleToNext(WaitingReason?)` — Task 6.
- ✅ Hotkeys: register both `9` and `0` — Task 8.
- ✅ Tray: split menu items — Task 7.
- ✅ MainPage: second debug button — Task 9.
- ✅ Tests for all dispatcher cases — Tasks 2–4.
- ✅ Tests for verifier classifier — Task 5.
- ✅ Manual smoke test for the multi-prompt scenario — Task 10.

**No spec gaps.**

**Placeholder scan:** none found. Every code step shows complete code. Every test shows the assertion explicitly. All file paths are absolute relative to the repo root.

**Type / name consistency:**
- `WaitingReason.Permission` / `WaitingReason.AwaitingInput` used identically across Tasks 1–9.
- `CyclePermissionsRequested` / `CycleAnyWaitingRequested` event names are consistent between `TrayIconService` (Task 7) and `App.xaml.cs` (Task 8).
- `OnCyclePermsClicked` / `OnCycleAnyWaitingClicked` handler names are consistent between XAML (Task 9 Step 1) and code-behind (Task 9 Step 2).
- `CycleToNext(WaitingReason?)` signature is consistent between `ClaudeWindowService` (Task 6) and call sites in App.xaml.cs (Task 8) and MainPage.xaml.cs (Task 9).
- `MenuIdCycleClaude` is repurposed (renamed semantically to "Cycle Pending Permissions" but the constant value `1003` is preserved). New constant `MenuIdCycleAnyWaiting = 1005` is unique vs the existing `1000–1004` range.
