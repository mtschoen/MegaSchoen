# Session-Watcher Status Detection — Accuracy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **STATUS (2026-05-31 wrap) — Phases 1 + 4.5 DONE; Phases 2/3/4/5 PENDING.** Executed this session on branch `feat/session-status-detection` (8 commits `7223b31`..`240b7a9`, 104/104 Claude.Core tests green, no upstream yet):
> - ✅ **Phase 1** — classifier fixes (elicitation_dialog→AwaitingInput, elicitation_complete/response→Working, deny-path characterization).
> - ✅ **Phase 4.5** — process-shape taxonomy + background `claude agents` sessions (`BackgroundSessionParser`, `ProcessResolver.EnumerateBackgroundClaudeSessions` via PEB cmdline read, `ClaudeWindow.SessionId`, authoritative-id pass in `ActiveSessionEnumerator`). Live-verified: 3 background workers now visible in `ClaudeSessionsCLI list --json`.
> - ⏳ **PENDING:** Phase 2 (in-process replay harness), Phase 3 (end-to-end replay script), Phase 4 (version stamp + stale-bridge guardrail), Phase 5 (full suite + **rebuild solution to redeploy the embedded bridge** — the live MAUI app does NOT yet reflect any of this). Known follow-up: a background session with no hook fired shows state `Unknown` (locate transcript by scanning slug dirs for `<id>.jsonl`).
> - In-phase `- [ ]` checkboxes for the DONE phases were NOT individually flipped; `git log --oneline 0fbd9fc..` is authoritative. Branch-finish pending: prune plan, fold taxonomy into CLAUDE.md, open PR as claude-code.

**Goal:** Make per-session Claude status detection (PendingPermission / AwaitingInput / Working / Idle) accurate and verifiable, with a deterministic replay harness that drives the real binaries, fix the unhandled mid-task question-prompt case, and add an always-visible version stamp + stale-binary guardrail so a stale hook-bridge deploy can never silently corrupt status again.

**Architecture:** The watcher's inputs are deterministic even though Claude isn't — it classifies from hook-event-driven state files, per-cwd process liveness, and a transcript tail-read fallback. We freeze real inputs into fixtures and replay them: (a) end-to-end through the shipped `ClaudeHookBridge.exe` + `ClaudeSessionsCLI list --json` via a PowerShell script, and (b) in-process via MSTest. Two env-gated seams (`MEGASCHOEN_STATE_DIR`, `MEGASCHOEN_FAKE_PROCESSES`) let the CLI run without a live `claude.exe`. Classifier fixes (elicitation_dialog → AwaitingInput; deny-path clearing) are driven by the harness as acceptance gate. A repo-wide version idiom (built-in .NET 8+ `SourceRevisionId`, SemVer+short-hash+`-dirty`) is surfaced in the MAUI lower-right and used for a startup app-vs-bridge version-divergence banner.

**Tech Stack:** .NET 10, MSTest, MAUI (Shell), MSBuild (`Directory.Build.props` + inline git target), PowerShell (replay script). Build with VS18 MSBuild (`vswhere -latest`), never VS2022's.

**Root cause (context for the worker):** Production `hook-bridge.log` showed ~35k `"ignoring event PostToolUse / idle_prompt"` lines from a **stale embedded bridge binary** (current source already handles both events). `settings.json` points hooks at the MAUI-embedded copy (`MegaSchoen/bin/x64/Debug/.../win-x64/ClaudeHookBridge.exe`), refreshed only by a full solution build. The capture tee (`Claude.Core/HookCapture.cs`) is already built and committed — this plan starts after it.

**Preconditions already done (do NOT redo):**
- `Claude.Core/HookCapture.cs` exists + wired into `ClaudeHookBridge/Program.cs` (env `MEGASCHOEN_HOOK_CAPTURE`).
- Branch `feat/session-status-detection` is checked out off `main`.
- Test infra exists: `Claude.Core.Tests/Fakes/FakeProcessLocator.cs` (has `List<ClaudeWindow> Sessions`), `Claude.Core.Tests/Fakes/ClaudeProjectsFixture.cs` (`Root`, `AddSession(slug, id, jsonlLine, mtimeUtc, creationTimeUtc?)`, `AddSubagent`, `Dispose`).

**Build/run reference (use these exact paths):**
- MSBuild: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe"`
- Build a single lib fast: `dotnet build Claude.Core/Claude.Core.csproj -c Debug`
- Run tests: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj -c Debug`
- Bridge exe (standalone): `ClaudeHookBridge/bin/Debug/net10.0-windows10.0.26100.0/ClaudeHookBridge.exe`
- CLI exe: `ClaudeSessionsCLI/bin/Debug/net10.0-windows10.0.26100.0/ClaudeSessionsCLI.exe`
- Embedded bridge (what settings.json runs): `MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ClaudeHookBridge.exe`

---

## Process shape taxonomy (added 2026-05-31)

Two launch paths coexist and **neither is the entry point** — the detector recognizes whichever process *shape* it finds and merges everything by real `session_id`. Confirmed live on the dev box 2026-05-31 (4 foreground + 2 background concurrently, after `claude agents` shipped as a research preview 2026-05-11):

| Shape | Launched by | Process tree | Identity evidence | Liveness signal | Window / Focus |
|---|---|---|---|---|---|
| **Foreground** | `claude` in a directory | `claude.exe` (cmdline `"claude"`), parent = shell (cmd/powershell/pwsh) | none on cmdline → transcript/cwd correlation (today's path) | per-cwd `claude.exe` present | real terminal window → raise it |
| **Background** | `claude agents` → `/bg` | `claude.exe daemon run` ▸ `claude.exe --bg-pty-host …pty-<shortid>` ▸ `claude.exe --session-id <GUID> --agent <name>` | **`--session-id <GUID>` on the worker cmdline → authoritative** | worker process present under the daemon | none (PTY is a named pipe) → Focus = attach/disabled |
| **Headless** | `claude -p`, unresolved `--resume` | `claude.exe` windowless, shell- or script-parented | transcript only | process present | none → disabled |

**The bug this exposes:** `ProcessResolver.EnumerateClaudeCliProcesses()` keeps only claude processes whose **parent is a shell** (`GetShellPids()` = cmd/powershell/pwsh). Background workers are parented by `--bg-pty-host`, grandparented by `daemon run` — so they are **silently dropped**: MegaSchoen cannot currently see *any* backgrounded agent session. The enumerator is additionally **cwd-keyed** (`ActiveSessionEnumerator` groups live processes by `WorkingDirectory` and skips any with an empty cwd), so even if a worker slipped past the shell filter, an empty/foreign worker cwd would still drop it.

**Design rules (load-bearing — do not let the nicer evidence tempt a rewrite):**
1. **Foreground correlation stays primary.** It is the info-poor, most-used path; the background `--session-id` is a *bonus precision path when present*, never a required one. Do not assume either entry point.
2. **Authoritative identity wins.** If a live process carries `--session-id`, trust it absolutely and skip the cwd→transcript guess for that session.
3. **Merge by real `session_id` regardless of shape.** Dedupe on id; a session somehow present in both shapes collapses to one.

Implementation lives in **Phase 4.5** below (purely additive; the foreground path is untouched).

---

## File Structure

**Create:**
- `Claude.Core/EnvironmentProcessLocator.cs` — env-gated `IClaudeProcessLocator` for replay (parses `MEGASCHOEN_FAKE_PROCESSES`).
- `Claude.Core/BuildInfo.cs` — runtime version reader (InformationalVersion → display string).
- `Directory.Build.props` (repo root) — version props + inline git-hash/-dirty target shared by all projects.
- `Claude.Core.Tests/Fixtures/SessionScenario.cs` — fixture model + JSON loader.
- `Claude.Core.Tests/Fixtures/sessions/*.json` — scenario fixtures.
- `Claude.Core.Tests/SessionStatusReplayTests.cs` — in-process data-driven replay over fixtures.
- `Claude.Core.Tests/EnvironmentProcessLocatorTests.cs` — unit tests for the env locator.
- `Claude.Core.Tests/BuildInfoTests.cs` — unit tests for the version reader.
- `.claude/scripts/simulate-session.ps1` — end-to-end replay script through real binaries.
- `MegaSchoen/Controls/VersionStampView.xaml(.cs)` — reusable lower-right version label.
- `Claude.Core/BackgroundSessionParser.cs` — pure `--session-id` worker-shape parser (Phase 4.5).
- `Claude.Core.Tests/BackgroundSessionParserTests.cs` — parser unit tests (Phase 4.5).

**Modify:**
- `Claude.Core/ProcessResolver.cs` — read process command line (extend the PEB reader); `EnumerateBackgroundClaudeSessions()` for daemon-tree workers (Phase 4.5).
- `Claude.Core/Models/ClaudeWindow.cs` — add optional `string? SessionId` (authoritative when set; null = derive the old way) (Phase 4.5).
- `Claude.Core/Windows/WindowsClaudeProcessLocator.cs` — merge foreground + background/daemon shapes into one `EnumerateLiveSessions()` (Phase 4.5).
- `Claude.Core/ActiveSessionEnumerator.cs` — trust an authoritative `SessionId` over cwd→transcript correlation; surface it even with no/foreign cwd (Phase 4.5).
- `Claude.Core/HookDispatcher.cs` — handle `elicitation_dialog`/`elicitation_complete`/`elicitation_response`; deny-path clearing.
- `Claude.Core.Tests/HookDispatcherTests.cs` — new transition tests.
- `ClaudeSessionsCLI/Commands/ListCommand.cs` — env-gated `MEGASCHOEN_STATE_DIR` + `MEGASCHOEN_FAKE_PROCESSES` in `BuildEnumerator()`.
- `MegaSchoen/MainPage.xaml`, `MegaSchoen/SessionsPage.xaml` — drop in `VersionStampView`.
- `MegaSchoen/App.xaml.cs` (or `Platforms/Windows/App.xaml.cs`) — startup app-vs-bridge version-divergence check.

---

## Phase 1: Classifier fixes (the real code gap)

The mid-task question prompt (`elicitation_dialog`) currently falls through to the ignored-Notification default, leaving the session labeled `Working`. Fix it test-first. This phase is pure `Claude.Core` — fast inner loop, no MAUI build.

### Task 1: Handle `elicitation_dialog` → AwaitingInput

**Files:**
- Modify: `Claude.Core/HookDispatcher.cs:33-44` (the `Notification` switch cases)
- Test: `Claude.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HookDispatcherTests.cs`:

```csharp
[TestMethod]
public void Notification_ElicitationDialog_UpsertsAwaitingInput()
{
    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Notification",
        NotificationType = "elicitation_dialog",
        SessionId = "s1",
        Cwd = "C:\\foo",
        Message = "Claude is asking a question"
    });

    var entries = _store.Read();
    Assert.IsTrue(entries.ContainsKey("s1"));
    Assert.AreEqual(WaitingReason.AwaitingInput, entries["s1"].Reason);
    Assert.AreEqual("Claude is asking a question", entries["s1"].Message);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Notification_ElicitationDialog_UpsertsAwaitingInput`
Expected: FAIL — session not upserted (falls through to the ignored-Notification default), `entries` empty.

- [ ] **Step 3: Add the handler case**

In `HookDispatcher.cs`, add a case alongside the existing `idle_prompt` case (after line 39):

```csharp
case "Notification" when payload.NotificationType == "elicitation_dialog":
    SetState(payload, WaitingReason.AwaitingInput, payload.Message);
    break;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Notification_ElicitationDialog_UpsertsAwaitingInput`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/HookDispatcher.cs Claude.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(sessions): classify elicitation_dialog as AwaitingInput"
```

### Task 2: Clear AwaitingInput when elicitation resolves

**Files:**
- Modify: `Claude.Core/HookDispatcher.cs` (Notification switch)
- Test: `Claude.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[TestMethod]
public void Notification_ElicitationComplete_OverwritesAwaitingInputWithWorking()
{
    _store.Upsert("s1", new SessionEntry
    {
        Cwd = "C:\\foo",
        NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        Reason = WaitingReason.AwaitingInput
    });

    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Notification",
        NotificationType = "elicitation_complete",
        SessionId = "s1",
        Cwd = "C:\\foo"
    });

    Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Notification_ElicitationComplete_OverwritesAwaitingInputWithWorking`
Expected: FAIL — reason stays `AwaitingInput` (elicitation_complete hits the ignored default).

- [ ] **Step 3: Add the handler case**

In `HookDispatcher.cs`, add before the catch-all `case "Notification":`:

```csharp
case "Notification" when payload.NotificationType is "elicitation_complete" or "elicitation_response":
    SetState(payload, WaitingReason.Working, message: null);
    break;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Notification_ElicitationComplete_OverwritesAwaitingInputWithWorking`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/HookDispatcher.cs Claude.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(sessions): clear AwaitingInput on elicitation_complete/response"
```

### Task 3: Document the deny-path reality (no code change unless confirmed)

**Context:** The spec hypothesized a denied permission may not clear `PendingPermission`. In the current model, a *denied* tool fires no `PostToolUse`, but Claude resumes (it tells the assistant the call was blocked) → the next `PreToolUse`/`PostToolUse`/`Stop` upserts a new state. So the latch clears on the *next* event, not instantly. There is no separate "denied" hook. Verify there is no infinite-stale path.

**Files:**
- Test: `Claude.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write the characterization test**

```csharp
[TestMethod]
public void Permission_ThenNextToolEvent_ClearsLatch_DenyPath()
{
    // Deny fires no PostToolUse for the denied call, but the session keeps
    // going: the next PreToolUse (or Stop) must move it off PendingPermission.
    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Notification", NotificationType = "permission_prompt",
        SessionId = "s1", Cwd = "C:\\foo", Message = "needs permission"
    });
    Assert.AreEqual(WaitingReason.Permission, _store.Read()["s1"].Reason);

    // user denies; assistant continues; next tool attempt:
    _dispatcher.Dispatch(new HookPayload { HookEventName = "PreToolUse", SessionId = "s1", Cwd = "C:\\foo" });

    Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason,
        "the next tool event after a deny clears the stale permission latch");
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Permission_ThenNextToolEvent_ClearsLatch_DenyPath`
Expected: PASS (current code already upserts Working on PreToolUse). If it FAILS, the deny path has a real bug — STOP and report; do not paper over it.

- [ ] **Step 3: Commit**

```bash
git add Claude.Core.Tests/HookDispatcherTests.cs
git commit -m "test(sessions): characterize deny-path permission latch clearing"
```

---

## Phase 2: In-process replay harness (fixtures + MSTest)

A data-driven test that replays labeled transition sequences through the real `HookDispatcher` + `StateStore` + `SessionStateClassifier`, asserting state after each step. Shares its fixtures with the end-to-end script in Phase 3.

### Task 4: Fixture model + loader

**Files:**
- Create: `Claude.Core.Tests/Fixtures/SessionScenario.cs`

- [x] **Step 1: Write the model + loader**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.Core.Tests.Fixtures;

public sealed class SessionScenario
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("steps")] public List<ScenarioStep> Steps { get; set; } = new();

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static SessionScenario Load(string path) =>
        JsonSerializer.Deserialize<SessionScenario>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Could not parse scenario: {path}");

    public static IEnumerable<string> FixtureFiles(string fixturesDir) =>
        Directory.EnumerateFiles(fixturesDir, "*.json").OrderBy(p => p);
}

public sealed class ScenarioStep
{
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("notificationType")] public string? NotificationType { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("delayMs")] public int DelayMs { get; set; }
    [JsonPropertyName("expectAfter")] public string ExpectAfter { get; set; } = "";
}
```

- [x] **Step 2: Verify it compiles**

Run: `dotnet build Claude.Core.Tests/Claude.Core.Tests.csproj -c Debug`
Expected: Build succeeded.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/Fixtures/SessionScenario.cs
git commit -m "test(sessions): add replay fixture model + loader"
```

### Task 5: Author the scenario fixtures

**Files:**
- Create: `Claude.Core.Tests/Fixtures/sessions/permission-approve.json`
- Create: `Claude.Core.Tests/Fixtures/sessions/elicitation-midtask.json`
- Create: `Claude.Core.Tests/Fixtures/sessions/turn-stop-awaiting.json`
- Create: `Claude.Core.Tests/Fixtures/sessions/deny-then-continue.json`

These files must be copied to output. Add to `Claude.Core.Tests.csproj` (inside an `<ItemGroup>`):

- [x] **Step 1: Wire fixtures into the test project output**

Add to `Claude.Core.Tests/Claude.Core.Tests.csproj`:

```xml
<ItemGroup>
  <None Include="Fixtures/sessions/*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [x] **Step 2: Write `permission-approve.json`**

```json
{
  "name": "permission-approve",
  "cwd": "C:/replay/permission-approve",
  "sessionId": "replay-perm-approve",
  "steps": [
    { "event": "UserPromptSubmit", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Notification", "notificationType": "permission_prompt", "message": "Claude needs your permission to use Bash", "delayMs": 0, "expectAfter": "PendingPermission" },
    { "event": "PostToolUse", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Stop", "delayMs": 0, "expectAfter": "AwaitingInput" }
  ]
}
```

- [x] **Step 3: Write `elicitation-midtask.json`**

```json
{
  "name": "elicitation-midtask",
  "cwd": "C:/replay/elicitation-midtask",
  "sessionId": "replay-elicit",
  "steps": [
    { "event": "UserPromptSubmit", "delayMs": 0, "expectAfter": "Working" },
    { "event": "PreToolUse", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Notification", "notificationType": "elicitation_dialog", "message": "Claude is asking a question", "delayMs": 0, "expectAfter": "AwaitingInput" },
    { "event": "Notification", "notificationType": "elicitation_complete", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Stop", "delayMs": 0, "expectAfter": "AwaitingInput" }
  ]
}
```

- [x] **Step 4: Write `turn-stop-awaiting.json`**

```json
{
  "name": "turn-stop-awaiting",
  "cwd": "C:/replay/turn-stop-awaiting",
  "sessionId": "replay-stop",
  "steps": [
    { "event": "UserPromptSubmit", "delayMs": 0, "expectAfter": "Working" },
    { "event": "PostToolUse", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Stop", "delayMs": 0, "expectAfter": "AwaitingInput" }
  ]
}
```

- [x] **Step 5: Write `deny-then-continue.json`**

```json
{
  "name": "deny-then-continue",
  "cwd": "C:/replay/deny-then-continue",
  "sessionId": "replay-deny",
  "steps": [
    { "event": "Notification", "notificationType": "permission_prompt", "message": "Claude needs your permission to use Bash", "delayMs": 0, "expectAfter": "PendingPermission" },
    { "event": "PreToolUse", "delayMs": 0, "expectAfter": "Working" },
    { "event": "Stop", "delayMs": 0, "expectAfter": "AwaitingInput" }
  ]
}
```

- [x] **Step 6: Commit**

```bash
git add Claude.Core.Tests/Fixtures/sessions/ Claude.Core.Tests/Claude.Core.Tests.csproj
git commit -m "test(sessions): author status-detection replay fixtures"
```

### Task 6: In-process replay test

**Files:**
- Create: `Claude.Core.Tests/SessionStatusReplayTests.cs`

This maps `ScenarioStep.Event`+`NotificationType` to a `HookPayload`, dispatches through `HookDispatcher`, then classifies via `ActiveSessionEnumerator` (so the assertion covers the full read path, not just the store). Uses the existing `ClaudeProjectsFixture` + `FakeProcessLocator`.

- [x] **Step 1: Write the test**

```csharp
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;
using Claude.Core.Tests.Fixtures;

namespace Claude.Core.Tests;

[TestClass]
public class SessionStatusReplayTests
{
    static string FixturesDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "sessions");

    public static IEnumerable<object[]> Scenarios()
    {
        foreach (var file in SessionScenario.FixtureFiles(FixturesDir))
        {
            yield return new object[] { Path.GetFileNameWithoutExtension(file), file };
        }
    }

    [TestMethod]
    [DynamicData(nameof(Scenarios), DynamicDataSourceType.Method)]
    public void Replay_StateMatchesExpectationAfterEachStep(string name, string path)
    {
        var scenario = SessionScenario.Load(path);
        using var fixture = new ClaudeProjectsFixture();
        var slug = SlugEncoder.Encode(scenario.Cwd);
        // A transcript must exist so the enumerator surfaces the session.
        fixture.AddSession(slug, scenario.SessionId,
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var dispatcher = new HookDispatcher(store);
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(new ClaudeWindow(
            100, WindowToken.Null, "", scenario.Cwd, DateTimeOffset.UtcNow));
        var transcriptPath = Path.Combine(fixture.Root, slug, $"{scenario.SessionId}.jsonl");

        for (var i = 0; i < scenario.Steps.Count; i++)
        {
            var step = scenario.Steps[i];
            dispatcher.Dispatch(new HookPayload
            {
                HookEventName = step.Event,
                NotificationType = step.NotificationType,
                Message = step.Message,
                SessionId = scenario.SessionId,
                Cwd = scenario.Cwd,
                TranscriptPath = transcriptPath
            });

            var snapshot = new ActiveSessionEnumerator(locator, store, fixture.Root)
                .Enumerate()
                .Single(s => s.SessionId == scenario.SessionId);

            Assert.AreEqual(
                Enum.Parse<SessionState>(step.ExpectAfter),
                snapshot.State,
                $"[{name}] step {i} ({step.Event}/{step.NotificationType}) expected {step.ExpectAfter}");
        }
    }
}
```

- [x] **Step 2: Run the test**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Replay_StateMatchesExpectationAfterEachStep`
Expected: PASS for all four scenarios (Phase 1 fixes make elicitation pass). If `elicitation-midtask` fails, Phase 1 was not completed.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/SessionStatusReplayTests.cs
git commit -m "test(sessions): in-process fixture replay over full read path"
```

---

## Phase 3: End-to-end replay through real binaries

Env-gated seams so the CLI runs with no live `claude.exe`, plus a PowerShell script that pipes each fixture step into the real `ClaudeHookBridge.exe` and asserts via `ClaudeSessionsCLI list --json`.

### Task 7: Env-gated process locator in Claude.Core

**Files:**
- Create: `Claude.Core/EnvironmentProcessLocator.cs`
- Create: `Claude.Core.Tests/EnvironmentProcessLocatorTests.cs`

- [x] **Step 1: Write the failing test**

```csharp
using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class EnvironmentProcessLocatorTests
{
    [TestMethod]
    public void Parse_JsonArray_YieldsWindowlessLiveSessionsPerCount()
    {
        var json = """[{"cwd":"C:/a","count":2},{"cwd":"C:/b","count":1}]""";
        var sessions = EnvironmentProcessLocator.Parse(json);

        Assert.AreEqual(3, sessions.Count);
        Assert.AreEqual(2, sessions.Count(s => s.WorkingDirectory == "C:/a"));
        Assert.AreEqual(1, sessions.Count(s => s.WorkingDirectory == "C:/b"));
        Assert.IsTrue(sessions.All(s => s.Window.IsZero), "replay procs are windowless");
    }

    [TestMethod]
    public void Parse_NullOrEmpty_YieldsNothing()
    {
        Assert.AreEqual(0, EnvironmentProcessLocator.Parse(null).Count);
        Assert.AreEqual(0, EnvironmentProcessLocator.Parse("").Count);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter EnvironmentProcessLocator`
Expected: FAIL — type does not exist.

- [x] **Step 3: Implement the locator**

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Claude.Core.Models;

namespace Claude.Core;

// Test/replay-only IClaudeProcessLocator selected when MEGASCHOEN_FAKE_PROCESSES
// is set (JSON: [{ "cwd": "...", "count": N }]). Emits windowless live sessions
// so the replay harness satisfies the per-cwd liveness gate with no real
// claude.exe. No-op (empty) when the variable is unset — never active in normal
// use. This is a documented test affordance, not a production code path.
public sealed class EnvironmentProcessLocator : IClaudeProcessLocator
{
    public const string EnvironmentVariable = "MEGASCHOEN_FAKE_PROCESSES";

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    readonly IReadOnlyList<ClaudeWindow> _sessions;

    public EnvironmentProcessLocator()
        => _sessions = Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions() => _sessions;

    public static IReadOnlyList<ClaudeWindow> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ClaudeWindow>();

        var specs = JsonSerializer.Deserialize<List<FakeProcSpec>>(json, Options)
            ?? new List<FakeProcSpec>();
        var result = new List<ClaudeWindow>();
        uint pid = 1;
        foreach (var spec in specs)
        {
            for (var i = 0; i < spec.Count; i++)
            {
                result.Add(new ClaudeWindow(
                    pid++, WindowToken.Null, "", spec.Cwd, DateTimeOffset.UtcNow));
            }
        }
        return result;
    }

    sealed class FakeProcSpec
    {
        [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";
        [JsonPropertyName("count")] public int Count { get; set; }
    }
}
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter EnvironmentProcessLocator`
Expected: PASS

- [x] **Step 5: Commit**

```bash
git add Claude.Core/EnvironmentProcessLocator.cs Claude.Core.Tests/EnvironmentProcessLocatorTests.cs
git commit -m "feat(sessions): env-gated process locator for replay harness"
```

### Task 8: Wire env seams into the CLI

**Files:**
- Modify: `ClaudeSessionsCLI/Commands/ListCommand.cs:29-38` (`BuildEnumerator`)

- [x] **Step 1: Replace `BuildEnumerator()`**

```csharp
static ActiveSessionEnumerator BuildEnumerator()
{
    // Replay/test seams (no-ops when the env vars are unset):
    //   MEGASCHOEN_FAKE_PROCESSES → run with no real claude.exe (windowless procs)
    //   MEGASCHOEN_STATE_DIR      → isolate the needy-sessions state directory
    IClaudeProcessLocator locator =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentProcessLocator.EnvironmentVariable))
            ? new EnvironmentProcessLocator()
#if WINDOWS
            : new WindowsClaudeProcessLocator();
#else
            : new Claude.Core.Linux.LinuxClaudeProcessLocator();
#endif

    var stateDir = Environment.GetEnvironmentVariable("MEGASCHOEN_STATE_DIR");
    var store = string.IsNullOrWhiteSpace(stateDir) ? new StateStore() : new StateStore(stateDir);
    return new ActiveSessionEnumerator(locator, store);
}
```

Note: `ActiveSessionEnumerator(locator, store)` uses the default projects root (`~/.claude/projects`); the replay's transcripts go in the *real* slug dir under a `C:/replay/...` cwd, which is harmless (throwaway). The harness asserts on `State`, which comes from the StateStore, so the transcript only needs to exist for surfacing.

- [x] **Step 2: Verify build**

Run: `dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -c Debug`
Expected: Build succeeded.

- [x] **Step 3: Commit**

```bash
git add ClaudeSessionsCLI/Commands/ListCommand.cs
git commit -m "feat(sessions): env-gated state-dir + fake-process seams in CLI"
```

### Task 9: The end-to-end replay script

**Files:**
- Create: `.claude/scripts/simulate-session.ps1`

The script needs the transcript to exist (so the CLI surfaces the session) AND the env seams set. It creates a throwaway transcript under the real projects root for each scenario cwd, then drives the bridge.

- [x] **Step 1: Write the script**

```powershell
#requires -Version 7
# End-to-end replay: pipe each fixture step into the REAL ClaudeHookBridge.exe,
# then assert ClaudeSessionsCLI list --json reports the expected State.
# Usage: pwsh .claude/scripts/simulate-session.ps1
[CmdletBinding()]
param(
    [string]$FixturesDir = "$PSScriptRoot/../../Claude.Core.Tests/Fixtures/sessions",
    [string]$Bridge = "$PSScriptRoot/../../ClaudeHookBridge/bin/Debug/net10.0-windows10.0.26100.0/ClaudeHookBridge.exe",
    [string]$Cli = "$PSScriptRoot/../../ClaudeSessionsCLI/bin/Debug/net10.0-windows10.0.26100.0/ClaudeSessionsCLI.exe"
)

$ErrorActionPreference = 'Stop'
foreach ($exe in @($Bridge, $Cli)) {
    if (-not (Test-Path $exe)) { throw "Missing binary: $exe (build the solution first)" }
}

$projectsRoot = Join-Path $HOME ".claude/projects"
$failures = 0
$scenarioFiles = Get-ChildItem -Path $FixturesDir -Filter *.json | Sort-Object Name

foreach ($file in $scenarioFiles) {
    $scenario = Get-Content $file.FullName -Raw | ConvertFrom-Json
    $stateDir = Join-Path ([System.IO.Path]::GetTempPath()) "replay-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $stateDir | Out-Null

    # Slug encode MUST mirror Claude.Core.SlugEncoder exactly: every char that
    # is not a letter or digit becomes a hyphen.
    $slug = ($scenario.cwd -replace '[^a-zA-Z0-9]', '-')
    $slugDir = Join-Path $projectsRoot $slug
    New-Item -ItemType Directory -Path $slugDir -Force | Out-Null
    $transcript = Join-Path $slugDir "$($scenario.sessionId).jsonl"
    '{"type":"assistant","message":{},"cwd":"' + ($scenario.cwd -replace '\\','\\\\') + '"}' | Set-Content $transcript

    $env:MEGASCHOEN_STATE_DIR = $stateDir
    $env:MEGASCHOEN_FAKE_PROCESSES = (ConvertTo-Json -Compress @(@{ cwd = $scenario.cwd; count = 1 }))

    Write-Host "`n=== $($scenario.name) ===" -ForegroundColor Cyan
    for ($i = 0; $i -lt $scenario.steps.Count; $i++) {
        $step = $scenario.steps[$i]
        $payload = @{
            session_id      = $scenario.sessionId
            cwd             = $scenario.cwd
            transcript_path = $transcript
            hook_event_name = $step.event
        }
        if ($step.notificationType) { $payload.notification_type = $step.notificationType }
        if ($step.message)          { $payload.message = $step.message }

        ($payload | ConvertTo-Json -Compress) | & $Bridge | Out-Null
        if ($step.delayMs -gt 0) { Start-Sleep -Milliseconds $step.delayMs }

        $json = & $Cli list --json | ConvertFrom-Json
        $row = $json | Where-Object { $_.SessionId -eq $scenario.sessionId } | Select-Object -First 1
        $actual = if ($row) { $row.State } else { "(absent)" }

        if ($actual -eq $step.expectAfter) {
            Write-Host ("  step {0} {1}/{2} -> {3} OK" -f $i, $step.event, $step.notificationType, $actual) -ForegroundColor Green
        } else {
            Write-Host ("  step {0} {1}/{2} -> {3} EXPECTED {4}" -f $i, $step.event, $step.notificationType, $actual, $step.expectAfter) -ForegroundColor Red
            $failures++
        }
    }

    Remove-Item -Recurse -Force $stateDir
    Remove-Item -Force $transcript -ErrorAction SilentlyContinue
}

Remove-Item Env:MEGASCHOEN_STATE_DIR, Env:MEGASCHOEN_FAKE_PROCESSES -ErrorAction SilentlyContinue
if ($failures -gt 0) { Write-Host "`n$failures step(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "`nAll replay steps passed" -ForegroundColor Green
```

- [x] **Step 2: Build the two binaries the script needs**

Run: `dotnet build ClaudeHookBridge/ClaudeHookBridge.csproj -c Debug && dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -c Debug`
Expected: both Build succeeded.

- [x] **Step 3: Run the script**

Run: `pwsh .claude/scripts/simulate-session.ps1`
Expected: "All replay steps passed". All 14 steps across 4 scenarios passed.

- [x] **Step 4: Confirm the SlugEncoder mirror is exact**

**CORRECTION (2026-05-31):** The plan's original comment here was wrong — `SlugEncoder.cs` does NOT map every non-alphanumeric char to `-`. It replaces ONLY `':'`, `'\'`, `'/'` with `-` (trimming trailing separators first). The script was written with the corrected `-replace '[:\\/]', '-'` regex to match the real encoder. The plan's `[^a-zA-Z0-9]` description was a documentation error. Also corrected: `ClaudeHookBridge/Program.cs` now honors `MEGASCHOEN_STATE_DIR` (was hardcoded to default `new StateStore()`), ensuring bridge and CLI share the same isolated state dir during replay.

- [x] **Step 5: Commit**

```bash
git add .claude/scripts/simulate-session.ps1
git commit -m "test(sessions): end-to-end replay script through real binaries"
```

---

## Phase 4: Version stamp idiom + stale-binary guardrail

### Task 10: Repo-wide version props (Directory.Build.props)

**Files:**
- Create: `Directory.Build.props` (repo root)

- [ ] **Step 1: Write `Directory.Build.props`**

```xml
<Project>
  <!-- Project-wide version idiom: SemVer base + git short-hash + -dirty.
       The .NET 8+ SDK appends SourceRevisionId (full commit hash) to
       AssemblyInformationalVersion automatically; this target shortens it and
       flags uncommitted local builds so a stale/dirty binary is obvious.
       See ~/.claude/notes/idioms_dotnet_version_stamp.md -->
  <PropertyGroup>
    <Version>0.1.0</Version>
    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
  </PropertyGroup>

  <Target Name="StampGitVersion" BeforeTargets="GetAssemblyVersion;CoreCompile">
    <Exec Command="git rev-parse --short=7 HEAD" ConsoleToMSBuild="true"
          ContinueOnError="true" StandardOutputImportance="low">
      <Output TaskParameter="ConsoleOutput" PropertyName="GitShortHash" />
    </Exec>
    <Exec Command="git status --porcelain" ConsoleToMSBuild="true"
          ContinueOnError="true" StandardOutputImportance="low">
      <Output TaskParameter="ConsoleOutput" PropertyName="GitStatusOutput" />
    </Exec>
    <PropertyGroup>
      <GitDirtySuffix Condition="'$(GitStatusOutput)' != ''">-dirty</GitDirtySuffix>
      <InformationalVersion Condition="'$(GitShortHash)' != ''">$(Version)+$(GitShortHash)$(GitDirtySuffix)</InformationalVersion>
      <InformationalVersion Condition="'$(GitShortHash)' == ''">$(Version)+nogit</InformationalVersion>
    </PropertyGroup>
  </Target>
</Project>
```

- [ ] **Step 2: Build a project and verify the stamp**

Run: `dotnet build Claude.Core/Claude.Core.csproj -c Debug`
Then verify the stamp:
`pwsh -c "[System.Reflection.Assembly]::LoadFrom((Resolve-Path Claude.Core/bin/Debug/net10.0/Claude.Core.dll)).GetCustomAttributes([System.Reflection.AssemblyInformationalVersionAttribute],\$false)[0].InformationalVersion"`
Expected: a string like `0.1.0+<7hex>` or `0.1.0+<7hex>-dirty` (dirty because the tree has uncommitted work mid-plan).

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "build: repo-wide version stamp (SemVer + git short-hash + dirty)"
```

### Task 11: Runtime version reader (BuildInfo)

**Files:**
- Create: `Claude.Core/BuildInfo.cs`
- Create: `Claude.Core.Tests/BuildInfoTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class BuildInfoTests
{
    [TestMethod]
    public void Version_IsNonEmpty()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(BuildInfo.Version));
    }

    [TestMethod]
    public void DescribeFor_NullAttribute_FallsBackToUnknown()
    {
        Assert.AreEqual("unknown", BuildInfo.Normalize(null));
    }

    [TestMethod]
    public void Normalize_StripsAssemblyMetadataPlusGarbage_KeepsVersionAndHash()
    {
        Assert.AreEqual("0.1.0+abc1234", BuildInfo.Normalize("0.1.0+abc1234"));
        Assert.AreEqual("0.1.0+abc1234-dirty", BuildInfo.Normalize("0.1.0+abc1234-dirty"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter BuildInfo`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement BuildInfo**

```csharp
using System.Reflection;

namespace Claude.Core;

// Reads the build version stamped by Directory.Build.props (SemVer + git
// short-hash + optional -dirty). Surface this in every app's UI so a stale or
// uncommitted binary is immediately visible. See
// ~/.claude/notes/idioms_dotnet_version_stamp.md
public static class BuildInfo
{
    public static string Version { get; } = ReadFor(Assembly.GetEntryAssembly());

    public static string VersionFor(Assembly? assembly) => ReadFor(assembly);

    static string ReadFor(Assembly? assembly) => Normalize(
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    // .NET may append "+<sourcerevision>" garbage when SourceRevisionId leaks
    // through despite our opt-out; keep "<version>+<hash>[-dirty]" intact and
    // drop anything after a second '+'.
    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "unknown";
        var firstPlus = informationalVersion.IndexOf('+');
        if (firstPlus < 0) return informationalVersion;
        var secondPlus = informationalVersion.IndexOf('+', firstPlus + 1);
        return secondPlus < 0 ? informationalVersion : informationalVersion[..secondPlus];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter BuildInfo`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/BuildInfo.cs Claude.Core.Tests/BuildInfoTests.cs
git commit -m "feat: BuildInfo runtime version reader"
```

### Task 12: Version-stamp control in the MAUI app

**Files:**
- Create: `MegaSchoen/Controls/VersionStampView.xaml`
- Create: `MegaSchoen/Controls/VersionStampView.xaml.cs`
- Modify: `MegaSchoen/MainPage.xaml`, `MegaSchoen/SessionsPage.xaml`

- [ ] **Step 1: Write the control XAML**

`MegaSchoen/Controls/VersionStampView.xaml`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/dotnet/2021/maui"
             x:Class="MegaSchoen.Controls.VersionStampView"
             HorizontalOptions="End" VerticalOptions="End"
             InputTransparent="True">
    <Label x:Name="VersionLabel"
           FontSize="11" Opacity="0.55"
           Margin="0,0,8,4"
           HorizontalTextAlignment="End" />
</ContentView>
```

- [ ] **Step 2: Write the code-behind**

`MegaSchoen/Controls/VersionStampView.xaml.cs`:

```csharp
using Claude.Core;

namespace MegaSchoen.Controls;

public partial class VersionStampView : ContentView
{
    public VersionStampView()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{BuildInfo.Version}";
    }
}
```

- [ ] **Step 3: Place it in both pages**

In `MegaSchoen/MainPage.xaml` and `MegaSchoen/SessionsPage.xaml`, ensure the page's root layout is (or is wrapped in) a `Grid`, and add as the LAST child so it overlays bottom-right. Add the namespace to the page root:

```xml
xmlns:controls="clr-namespace:MegaSchoen.Controls"
```

and as the final child of the root `Grid`:

```xml
<controls:VersionStampView />
```

If a page's root is a `ScrollView`/`VerticalStackLayout` (not a Grid), wrap the existing root in:

```xml
<Grid>
    <!-- existing root content here -->
    <controls:VersionStampView />
</Grid>
```

- [ ] **Step 4: Build the MAUI app (VS18 MSBuild) and run it**

Run: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -v:m`
Expected: 0 Error(s). Then launch `MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/MegaSchoen.exe` and visually confirm a small `v0.1.0+<hash>` label in the lower-right of both pages. (Kill any running MegaSchoen.exe first — SingleInstanceService swallows new launches.)

- [ ] **Step 5: Commit**

```bash
git add MegaSchoen/Controls/VersionStampView.xaml MegaSchoen/Controls/VersionStampView.xaml.cs MegaSchoen/MainPage.xaml MegaSchoen/SessionsPage.xaml
git commit -m "feat(ui): lower-right version stamp on every page"
```

### Task 13: Startup app-vs-bridge version-divergence banner

**Files:**
- Modify: `MegaSchoen/App.xaml.cs` (or `MegaSchoen/Platforms/Windows/App.xaml.cs` if startup wiring lives there — grep for the existing startup zombie-sweep call and put it adjacent)

The embedded bridge and the app are built together, so their `BuildInfo` versions should match. A mismatch means the embedded copy is stale (the exact 35k-event failure). Read the embedded bridge's assembly version without launching it.

- [ ] **Step 1: Add the check helper to Claude.Core**

Append to `Claude.Core/BuildInfo.cs`:

```csharp
// Returns the InformationalVersion stamped into another assembly file (e.g. the
// embedded ClaudeHookBridge.dll) without loading it into the running process.
public static string VersionOfFile(string assemblyPath)
{
    if (!File.Exists(assemblyPath)) return "missing";
    try
    {
        // FileVersionInfo.ProductVersion carries InformationalVersion for SDK builds.
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(assemblyPath);
        return Normalize(info.ProductVersion);
    }
    catch
    {
        return "unreadable";
    }
}
```

- [ ] **Step 2: Add a test for VersionOfFile against the running assembly**

Add to `Claude.Core.Tests/BuildInfoTests.cs`:

```csharp
[TestMethod]
public void VersionOfFile_MissingPath_ReturnsMissing()
{
    Assert.AreEqual("missing", BuildInfo.VersionOfFile(
        Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.dll")));
}

[TestMethod]
public void VersionOfFile_OwnAssembly_MatchesRuntimeVersion()
{
    var path = typeof(BuildInfo).Assembly.Location;
    Assert.AreEqual(BuildInfo.Version, BuildInfo.VersionOfFile(path));
}
```

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter VersionOfFile`
Expected: PASS

- [ ] **Step 3: Wire the startup banner**

Find the existing startup sweep (grep `Startup zombie sweep` / the call site that runs at app launch). Adjacent to it, add:

```csharp
// Stale-binary guardrail: settings.json runs the MAUI-embedded ClaudeHookBridge
// copy. If its version diverges from the app's, the embedded copy was not
// rebuilt and status detection will be wrong (see the 35k stale-event incident).
void CheckBridgeFreshness()
{
    var appVersion = Claude.Core.BuildInfo.Version;
    // The CopyClaudeHookBridge target drops ClaudeHookBridge.dll next to the app.
    var bridgeDll = Path.Combine(AppContext.BaseDirectory, "ClaudeHookBridge.dll");
    var bridgeVersion = Claude.Core.BuildInfo.VersionOfFile(bridgeDll);

    if (bridgeVersion != appVersion)
    {
        Claude.Core.Logger.Log($"STALE BRIDGE: app={appVersion} bridge={bridgeVersion}");
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is not null)
            {
                await page.DisplayAlert(
                    "Hook bridge is stale",
                    $"App is {appVersion} but the embedded ClaudeHookBridge is {bridgeVersion}. " +
                    "Rebuild the solution (VS18 MSBuild) so session status detection is correct.",
                    "OK");
            }
        });
    }
}
```

Call `CheckBridgeFreshness();` from the same startup path. (`ClaudeHookBridge.dll` is copied next to the app by the `CopyClaudeHookBridge` target, so `AppContext.BaseDirectory` is correct.)

- [ ] **Step 4: Build the solution and smoke-test the banner**

Run: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -v:m`
Expected: 0 Error(s). After a clean solution build app and bridge versions match → NO banner. To prove the banner fires, temporarily touch the bridge's version (or test `VersionOfFile` returns "missing" by renaming the embedded `ClaudeHookBridge.dll`) — confirm the alert appears, then restore. Document the result; do not leave the bridge renamed.

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/BuildInfo.cs Claude.Core.Tests/BuildInfoTests.cs MegaSchoen/App.xaml.cs
git commit -m "feat: startup stale-bridge version-divergence banner"
```

### Task 14: Wire `ClaudeHookBridge check` into CI/build verification

**Files:**
- Modify: the CI workflow that builds the solution (grep `.github/workflows` / `.gitea` for the existing build job)

- [ ] **Step 1: Locate the CI build job**

Run: `ls .github/workflows .gitea/workflows 2>/dev/null` and read the file that runs the solution build + tests.

- [ ] **Step 2: Add a post-build bridge-install check step**

After the build + test steps, add a step that runs the freshly-built bridge's `check` verb against a throwaway settings file is NOT meaningful in CI (no installed hooks). Instead, assert the embedded bridge version equals the app version (the guardrail's machine half), e.g. a small step:

```yaml
- name: Verify embedded bridge is fresh
  shell: pwsh
  run: |
    $app = "MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/MegaSchoen.dll"
    $bridge = "MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ClaudeHookBridge.dll"
    $av = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductVersion
    $bv = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($bridge).ProductVersion
    if ($av -ne $bv) { Write-Error "Embedded bridge stale: app=$av bridge=$bv"; exit 1 }
    Write-Host "Bridge fresh: $bv"
```

If no CI workflow exists in the repo, SKIP this task and note it — do not invent a CI system.

- [ ] **Step 3: Commit (if applicable)**

```bash
git add <workflow-file>
git commit -m "ci: fail build if embedded bridge version is stale"
```

---

## Phase 4.5: Process-shape-aware enumeration (foreground + background/daemon)

> Added 2026-05-31 after confirming the supervisor/daemon model live on the dev box. See **Process shape taxonomy** above. This phase makes MegaSchoen *see* backgrounded `claude agents` sessions (currently dropped by the shell-parent filter) and trust their on-cmdline `--session-id` as authoritative identity. **Purely additive** — the foreground path is untouched. Tasks are numbered 16+ so the existing Phase 5 Task 15 keeps its number; execute this phase after Phase 1's fast win and before the Phase 5 redeploy.

**Why separate from Phases 1–3:** those harden the *classifier* (state from hook events). This phase widens *enumeration* (which sessions exist at all). Orthogonal.

### Task 16: Pure `--session-id` worker-shape parser

A pure, OS-free function so parsing is unit-tested with no live process. Recognizes the **leaf worker** shape and extracts the GUID; rejects the daemon and pty-host shapes (the pty-host command line *contains* `--session-id` after a `--`, but only the leaf worker process is the session).

**Files:**
- Create: `Claude.Core/BackgroundSessionParser.cs`
- Create: `Claude.Core.Tests/BackgroundSessionParserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class BackgroundSessionParserTests
{
    [TestMethod]
    public void Worker_CommandLine_YieldsSessionId()
    {
        var cmd = @"C:\Users\me\.local\bin\claude.exe --session-id 375e9c68-62ae-4146-a52c-be6645a6575c --agent claude";
        Assert.IsTrue(BackgroundSessionParser.TryParseWorkerSessionId(cmd, out var id));
        Assert.AreEqual("375e9c68-62ae-4146-a52c-be6645a6575c", id);
    }

    [TestMethod]
    public void PtyHost_CommandLine_IsNotAWorker()
    {
        // Contains --session-id after the `--`, but it is the host, not the session.
        var cmd = @"C:\x\claude.exe --bg-pty-host \\.\pipe\cc-daemon-x-pty-375e9c68 120 30 -- C:\x\claude.exe --session-id 375e9c68-62ae-4146-a52c-be6645a6575c --agent claude";
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(cmd, out _));
    }

    [TestMethod]
    public void DaemonAndForegroundAndNull_AreNotWorkers()
    {
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(@"C:\x\claude.exe daemon run --origin transient", out _));
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId("claude", out _));
        Assert.IsFalse(BackgroundSessionParser.TryParseWorkerSessionId(null, out _));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter BackgroundSessionParser`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the parser**

```csharp
namespace Claude.Core;

// Pure parser for the background ("claude agents" / /bg) worker process shape:
//   claude.exe --session-id <GUID> --agent <name>
// spawned under  claude.exe daemon run ▸ claude.exe --bg-pty-host …
// The worker carries its real session_id on the command line — authoritative
// identity, no transcript correlation needed. The pty-host shares the GUID in
// its pipe name and re-spawns the worker after a `--`, so reject any command
// line that is itself a --bg-pty-host or the daemon (only the leaf worker is
// the session).
public static class BackgroundSessionParser
{
    public static bool TryParseWorkerSessionId(string? commandLine, out string sessionId)
    {
        sessionId = "";
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (commandLine.Contains("--bg-pty-host", StringComparison.Ordinal)) return false;
        if (commandLine.Contains("daemon run", StringComparison.Ordinal)) return false;

        const string flag = "--session-id";
        var index = commandLine.IndexOf(flag, StringComparison.Ordinal);
        if (index < 0) return false;

        var rest = commandLine[(index + flag.Length)..].TrimStart();
        var end = rest.IndexOfAny(new[] { ' ', '\t' });
        var token = (end < 0 ? rest : rest[..end]).Trim().Trim('"');
        if (!Guid.TryParse(token, out var guid)) return false;

        sessionId = guid.ToString("D");
        return true;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter BackgroundSessionParser`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/BackgroundSessionParser.cs Claude.Core.Tests/BackgroundSessionParserTests.cs
git commit -m "feat(sessions): pure parser for background worker --session-id shape"
```

### Task 17: Read command line from the OS + enumerate background workers

Extend the existing PEB-reading infrastructure (the same `OpenProcess` / `NtQueryInformationProcess` / `ReadProcessMemory` pattern as `TryGetProcessCwd`) to also pull the command line, then enumerate daemon-tree workers. **The OS enumeration is not unit-testable deterministically** (it depends on live processes); deterministic coverage stays in Task 16's parser. This task's gate is a live verification against the two background sessions running on the dev box.

**Files:**
- Modify: `Claude.Core/Models/ClaudeWindow.cs`
- Modify: `Claude.Core/ProcessResolver.cs`

- [ ] **Step 1: Add `SessionId` to `ClaudeWindow`**

In `Claude.Core/Models/ClaudeWindow.cs`, add a trailing optional field (default `null`) and update the comment:

```csharp
// ProcessId is the claude.exe CLI process; Window/Title belong to the parent
// terminal hosting it. StartTimeUtc disambiguates JSONLs when sessions share a
// cwd. SessionId is set ONLY for background/daemon workers that carry
// --session-id on their command line (authoritative identity); null for
// foreground/headless sessions whose identity is derived downstream.
public readonly record struct ClaudeWindow(
    uint ProcessId,
    WindowToken Window,
    string Title,
    string? WorkingDirectory,
    DateTimeOffset StartTimeUtc = default,
    string? SessionId = null);
```

Adding a trailing optional positional parameter is source-compatible with the existing 5-arg construction sites (`WindowsClaudeProcessLocator`, `EnvironmentProcessLocator`, tests). Confirm a build still passes before continuing.

- [ ] **Step 2: Add `TryGetProcessCommandLine` to `ProcessResolver`**

Mirror `TryGetProcessCwd` exactly, but read the **CommandLine** `UNICODE_STRING` from `ProcessParameters`. On 64-bit it lives at `ProcessParameters + 0x70` (`Length` at +0x00, `Buffer` at +0x08). Cap at 8192 chars (worker command lines are short; renderer/helper ones are long and irrelevant). Return `null` on any failure — this is best-effort.

> ⚠️ **Verify the 0x70 offset before trusting it.** The `RTL_USER_PROCESS_PARAMETERS.CommandLine` offset can vary across Windows builds. Step 4 dumps live command lines; if they come back empty/garbled, dump candidate offsets (0x70 is standard for Win10/11 x64) or fall back to a one-shot `System.Management` `Win32_Process` WMI query (`SELECT ProcessId,ParentProcessId,CommandLine FROM Win32_Process WHERE Name='claude.exe'`). WMI is simpler but slower and adds the `System.Management` dependency — prefer the PEB read on this hot (re-enumerated-per-FS-event) path; only fall back if the offset proves unstable. Record which approach shipped.

- [ ] **Step 3: Add `EnumerateBackgroundClaudeSessions()` to `ProcessResolver`**

```csharp
// Background ("claude agents" / /bg) sessions: claude.exe workers that carry
// --session-id on the command line, parented by a --bg-pty-host under the
// daemon. They fail the shell-parent filter in EnumerateClaudeCliProcesses, so
// enumerate them separately and carry the authoritative session id forward.
public static List<ClaudeCliProcess> EnumerateBackgroundClaudeSessions(out Dictionary<uint, string> sessionIdByPid)
{
    sessionIdByPid = new Dictionary<uint, string>();
    var results = new List<ClaudeCliProcess>();
    foreach (var process in Process.GetProcessesByName("claude"))
    {
        using (process)
        {
            try
            {
                var pid = (uint)process.Id;
                var commandLine = TryGetProcessCommandLine(pid);
                if (!BackgroundSessionParser.TryParseWorkerSessionId(commandLine, out var sessionId)) continue;
                var parentPid = TryGetParentPid(pid) ?? 0;
                var cwd = TryGetProcessCwd(pid);
                var startTime = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                results.Add(new ClaudeCliProcess(pid, parentPid, cwd, startTime));
                sessionIdByPid[pid] = sessionId;
            }
            catch (Exception exception)
            {
                Logger.Log($"EnumerateBackgroundClaudeSessions skipping pid {process.Id}: {exception.Message}");
            }
        }
    }
    return results;
}
```

- [ ] **Step 4: LIVE verification (dev box, requires ≥1 backgrounded `claude agents` session)**

Build `Claude.Core` and run a throwaway dump (e.g. a `dotnet script` or a temporary CLI verb) that prints each background worker's `pid`, parsed `session_id`, and `cwd`. Cross-check against `Get-CimInstance Win32_Process -Filter "Name='claude.exe'" | Select ProcessId,CommandLine`. Confirm:
  1. **Every** `--session-id` worker is found (and the daemon + pty-host are NOT).
  2. The parsed `session_id` matches the GUID in the command line.
  3. **What the worker's `cwd` actually is** — the dispatched task folder, or the daemon's home dir, or empty/unreadable. **Record this in the plan** — it decides how Task 19 surfaces background sessions (cwd-keyed if the task folder; id-only pass if home/empty).

If `cwd` comes back as the home dir or empty for a task that targeted a specific folder, Task 19's id-only pass (below) is mandatory, not optional.

> **Live result (2026-05-31, 3 background workers):** the PEB command-line read works (correct session ids; daemon + pty-host correctly excluded; 0x70 offset confirmed). cwd is **unreliable**: one worker reported its real task folder (a `…/.claude/worktrees/…` worktree), but two reported `C:\Users\mtsch` (the daemon/home dir) and would collide in the same cwd bucket. **Conclusion: Task 19's authoritative-id pass is mandatory** — cwd-keying alone cannot place background sessions.

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/Models/ClaudeWindow.cs Claude.Core/ProcessResolver.cs
git commit -m "feat(sessions): enumerate background daemon-tree workers with authoritative session id"
```

### Task 18: Merge both shapes in `WindowsClaudeProcessLocator`

**Files:**
- Modify: `Claude.Core/Windows/WindowsClaudeProcessLocator.cs`

- [ ] **Step 1: Append background sessions to `EnumerateLiveSessions()`**

After building the foreground `result` list (current lines 13–26), append the background workers as **windowless** `ClaudeWindow`s carrying their authoritative `SessionId`:

```csharp
var backgroundProcs = ProcessResolver.EnumerateBackgroundClaudeSessions(out var sessionIdByPid);
foreach (var process in backgroundProcs)
{
    result.Add(new ClaudeWindow(
        ProcessId: process.Pid,
        Window: WindowToken.Null,        // PTY is a named pipe — no focusable window
        Title: string.Empty,
        WorkingDirectory: process.WorkingDirectory,
        StartTimeUtc: process.StartTimeUtc,
        SessionId: sessionIdByPid[process.Pid]));
}
return result;
```

No dedup needed: foreground enumeration filters to shell-parented processes; background workers are daemon-parented, so the two sets are disjoint (the daemon and pty-host processes themselves match neither and are correctly excluded).

- [ ] **Step 2: Build**

Run: `dotnet build Claude.Core/Claude.Core.csproj -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Claude.Core/Windows/WindowsClaudeProcessLocator.cs
git commit -m "feat(sessions): surface background sessions (windowless) from the locator"
```

### Task 19: `ActiveSessionEnumerator` trusts authoritative `SessionId`

**Contract:** a live process carrying `SessionId` surfaces *that exact id* as a session — even when its cwd has no transcript dir, and regardless of the per-cwd freshest-N cap. State and transcript are resolved by id from the StateStore + disk. The foreground cwd-keyed path is unchanged.

**Files:**
- Modify: `Claude.Core/ActiveSessionEnumerator.cs`
- Test: `Claude.Core.Tests/ActiveSessionEnumeratorTests.cs` (or the existing enumerator test file — grep for the `FakeProcessLocator` usage)

- [ ] **Step 1: Write the failing test (deterministic, via `FakeProcessLocator`)**

```csharp
[TestMethod]
public void Enumerate_AuthoritativeSessionId_SurfacesEvenWithNoTranscript()
{
    using var fixture = new ClaudeProjectsFixture();   // NO transcript added for this id
    var store = new StateStore(Path.Combine(fixture.Root, "state"));
    store.Upsert("bg-375e9c68", new SessionEntry
    {
        Cwd = "C:\\work\\proj",
        NotifiedAt = DateTimeOffset.UtcNow,
        Reason = WaitingReason.Working
    });

    var locator = new FakeProcessLocator();
    locator.Sessions.Add(new ClaudeWindow(
        ProcessId: 5000, Window: WindowToken.Null, Title: "",
        WorkingDirectory: "C:\\work\\proj", StartTimeUtc: DateTimeOffset.UtcNow,
        SessionId: "bg-375e9c68"));

    var snapshots = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

    var session = snapshots.SingleOrDefault(s => s.SessionId == "bg-375e9c68");
    Assert.IsNotNull(session, "authoritative-id session must surface without a transcript");
    Assert.AreEqual(SessionState.Working, session!.State);
}
```

(If `FakeProcessLocator.Sessions` entries are constructed positionally elsewhere, the new trailing `SessionId` arg is optional — existing rows stay foreground.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Enumerate_AuthoritativeSessionId`
Expected: FAIL — the cwd-keyed path needs a transcript on disk to surface a session, so the authoritative-id session is absent.

- [ ] **Step 3: Add an id-first pass in `Enumerate()`**

Before (or after) the cwd loop, add a pass over authoritative-id live processes that guarantees each is represented, deduping by id against whatever the cwd loop already produced. Recommended shape (adjust to the cwd finding from Task 17 Step 4):

```csharp
// Authoritative-id pass: background/daemon workers carry their real session_id
// on the command line. Trust it directly — surface the session by id even when
// its cwd has no transcript dir or the freshest-N cap would have dropped it.
// (Design rule 2: authoritative identity wins over cwd→transcript correlation.)
var alreadyEmitted = new HashSet<string>(snapshots.Select(s => s.SessionId), StringComparer.OrdinalIgnoreCase);
foreach (var process in liveProcesses)
{
    if (process.SessionId is not { } id || !alreadyEmitted.Add(id)) continue;

    var entry = stateBySessionId.TryGetValue(id, out var e) ? e : null;
    var cwd = NormalizeCwd(entry?.Cwd ?? process.WorkingDirectory ?? "");
    var slugDir = Path.Combine(_projectsRoot, SlugEncoder.Encode(cwd));
    var transcriptPath = entry?.TranscriptPath
        ?? Path.Combine(slugDir, $"{id}.jsonl");
    var hasFile = File.Exists(transcriptPath);
    var lastWrite = hasFile ? File.GetLastWriteTimeUtc(transcriptPath) : entry?.NotifiedAt.UtcDateTime ?? DateTime.UtcNow;

    snapshots.Add(new SessionSnapshot(
        SessionId: id,
        Cwd: cwd,
        TranscriptPath: hasFile ? transcriptPath : "",
        LastActivityUtc: new DateTimeOffset(lastWrite, TimeSpan.Zero),
        State: SessionStateClassifier.Classify(entry, hasFile ? transcriptPath : ""),
        PendingMessage: entry?.Message,
        Window: process.Window,                 // Null for background → Focus disabled
        WindowTitle: null,
        Subagents: hasFile ? EnumerateSubagents(slugDir, id) : Array.Empty<SubagentSnapshot>()));
}

snapshots.Sort(CompareForDisplay);
```

This dedupes against the cwd loop (so a background worker whose cwd *does* have a transcript isn't double-listed), and is the single source of truth for background sessions whose worker cwd is home/empty. Keep it minimal — do not refactor the foreground loop.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter Enumerate_AuthoritativeSessionId`
Expected: PASS. Then run the whole enumerator test class to confirm no foreground regression.

- [ ] **Step 5: Commit**

```bash
git add Claude.Core/ActiveSessionEnumerator.cs Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "feat(sessions): trust authoritative --session-id over cwd correlation"
```

### Task 20: Live end-to-end — see the background sessions in the CLI

- [ ] **Step 1: Build the CLI and list with a real backgrounded session**

With ≥1 `claude agents` background session running, run:
`dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -c Debug` then
`ClaudeSessionsCLI/bin/Debug/net10.0-windows10.0.26100.0/ClaudeSessionsCLI.exe list --json`
Expected: each background session appears with its real `SessionId`, correct `State`, and `Window` zero / Focus disabled. Cross-check the ids against `Get-CimInstance Win32_Process`. If a session is missing, debug against Task 17 (enumeration) or Task 19 (surfacing) — not the CLI.

- [ ] **Step 2: Note the result in the plan and proceed to Phase 5.**

---

## Phase 5: Full verification + deploy the fixed bridge

### Task 15: Whole-suite green + redeploy embedded bridge

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test MegaSchoen.sln -c Debug` (or per-test-project if the sln test run is flaky on MAUI TFMs: `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj -c Debug && dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj -c Debug`)
Expected: all PASS.

- [ ] **Step 2: Run the end-to-end replay**

Run: `pwsh .claude/scripts/simulate-session.ps1`
Expected: "All replay steps passed".

- [ ] **Step 3: Solution build to redeploy the embedded bridge**

Run: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -v:m`
Expected: 0 Error(s). This refreshes `MegaSchoen/bin/x64/.../win-x64/ClaudeHookBridge.exe` (the one `settings.json` invokes) with all the classifier fixes.

- [ ] **Step 4: Confirm the deployed bridge handles elicitation**

Run:
```bash
EMB="MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ClaudeHookBridge.exe"
TMP=$(mktemp -d)
printf '%s' '{"session_id":"verify-elicit","cwd":"C:/tmp/v","hook_event_name":"Notification","notification_type":"elicitation_dialog","message":"q"}' | "$EMB"
cat "$LOCALAPPDATA/MegaSchoen/needy-sessions/verify-elicit.json"
rm -f "$LOCALAPPDATA/MegaSchoen/needy-sessions/verify-elicit.json"; rm -rf "$TMP"
```
Expected: the JSON shows `"reason"` = AwaitingInput (numeric enum value 1), proving the deployed binary has the fix.

- [ ] **Step 5: Run aislop gate**

Run: `aislop scan .` then `aislop ci .`
Expected: gate passes (per CLAUDE.md quality gate). Address findings before finishing.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore(sessions): verify suite + replay green; redeploy fixed bridge"
```

---

## Post-implementation (branch finish — handled by finishing-a-development-branch skill)

- Write `~/.claude/notes/idioms_dotnet_version_stamp.md` capturing the Directory.Build.props + BuildInfo pattern for reuse across projects.
- Fold durable insight into `CLAUDE.md` (the elicitation handling + the stale-bridge guardrail + the **process-shape taxonomy**: foreground vs background/daemon, `--session-id` authoritative identity) and delete this plan.
- Update memory `project_session_id_enumeration` / add a status-detection note.
- Open PR as `claude-code` per CLAUDE.md Gitea conventions.
