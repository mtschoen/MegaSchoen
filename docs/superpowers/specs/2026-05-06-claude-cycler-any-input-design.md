# Claude Window Cycler — Any-Input Cycle & Verifier Hardening

**Date:** 2026-05-06
**Status:** Approved for planning
**Scope:** Follow-up to [2026-04-24-claude-window-cycler-needy-verification-design.md](2026-04-24-claude-window-cycler-needy-verification-design.md). Adds a second cycle mode for sessions waiting on *any* input (not just permission prompts), and hardens the liveness verifier to fix a multi-prompt bug exposed by real use.

## Context

Today the cycler tracks only sessions waiting on a permission prompt. `Ctrl+Alt+9` cycles through them so the user can fan out yes/no responses quickly. In practice the user also wants a broader cycle: every Claude session sitting idle and waiting for input — both permission-pending sessions *and* sessions whose turn ended (the green-icon state in Claude Code). That broader cycle becomes `Ctrl+Alt+0`, with parallel UI affordances.

While scoping this, two bugs in the existing perms-only flow surfaced:

1. **Multi-permission-in-one-session loss.** When two permission prompts are pending in the same session (parallel tools, or sequential prompts inside one turn), the user approves the first, `PostToolUse` fires for the resolved tool, and `HookDispatcher` deletes the entire session entry — even though the second prompt is still pending. The session vanishes from the cycle.
2. **Verifier false-positive eviction.** `SessionLivenessVerifier` evicts entries whenever transcript modtime > `NotifiedAt + grace`. If Claude Code touches the transcript for any reason during a multi-prompt session (writes from the resolved tool's run), the entry gets evicted prematurely.

Both are addressed here as part of the same change.

## Problems

1. No way to cycle "all sessions awaiting input" — only the permission subset.
2. `HookDispatcher.PostToolUse → delete` is too aggressive: clears the entry while sibling permissions still pend.
3. `SessionLivenessVerifier` uses transcript modtime alone, which can't distinguish "session resolved" from "another tool in the same session is running."
4. UI affordances (button, tray menu item) for the broader cycle don't exist.

## Non-goals

- Changing storage format (still single JSON file, still keyed by `session_id`).
- Cross-platform support; remains Windows-only.
- Tracking per-prompt identity. Hooks don't carry stable per-prompt IDs and we won't synthesize them.
- Auto-approving prompts.
- A live session list UI inside MegaSchoen.

## Approach

### Data model: tag entries with a reason

```csharp
public enum WaitingReason
{
    Permission,
    AwaitingInput
}

public sealed class SessionEntry
{
    public string Cwd { get; set; } = "";
    public string? TranscriptPath { get; set; }
    public DateTimeOffset NotifiedAt { get; set; }
    public string? Message { get; set; }
    public WaitingReason Reason { get; set; }   // NEW; default = Permission
}
```

Still one entry per `session_id`. Old persisted entries (no `reason` field) deserialize to `Permission` by default — which is the safe interpretation for the only kind of entry the previous version wrote.

`NeedySessionsFile.Version` stays at `1`. Adding an enum field with a benign default does not require a schema bump.

### Dispatcher: redefine the lifecycle

| Event | Old behavior | New behavior |
|---|---|---|
| `Notification(permission_prompt)` | upsert | upsert with `Reason=Permission` |
| `Stop` | delete | **upsert with `Reason=AwaitingInput`** |
| `UserPromptSubmit` | delete | delete (any reason — user is responding) |
| `SessionEnd` | delete | delete |
| `PostToolUse` | delete | **drop the case entirely** |

Two structural changes:

- **`Stop` flips from delete to upsert.** A `Stop` means the model has finished its turn and the session is now sitting idle, which is exactly the AwaitingInput state we want surfaced by `Ctrl+Alt+0`.
- **`PostToolUse` is dropped.** Its original purpose was to clear stale permission entries when an approved tool ran mid-turn without a `Stop` or `UserPromptSubmit`. The verifier already handles that case via transcript modtime. Keeping `PostToolUse` introduces the multi-prompt deletion bug; dropping it removes the bug at the cost of leaving entries visible for ~grace seconds longer in the single-prompt-approved case (acceptable; the verifier evicts them on next cycle).

`SettingsJsonInstaller` does not change — it already installs all five hook events, including `Stop`.

### Verifier v2: tail-read JSONL, classify by entry type

`SessionLivenessVerifier.IsStillWaiting(entry)` becomes a two-tier check:

```csharp
public bool IsStillWaiting(SessionEntry entry)
{
    if (string.IsNullOrEmpty(entry.TranscriptPath) || !File.Exists(entry.TranscriptPath))
        return false;

    var modtime = File.GetLastWriteTimeUtc(entry.TranscriptPath);
    var threshold = entry.NotifiedAt.UtcDateTime + _grace;

    // Fast path: transcript hasn't been touched since notification → definitely pending.
    if (modtime <= threshold)
        return true;

    // Slow path: transcript was touched. Read the last entry to decide.
    return ClassifyLastEntry(entry.TranscriptPath) == LastEntryClass.SessionPending;
}
```

`ClassifyLastEntry` opens the transcript with `FileShare.ReadWrite | FileShare.Delete`, seeks to roughly the last 4 KB, finds the last `\n` boundary, and parses the trailing JSON object.

Classification table:

| Last entry shape | Class | Rationale |
|---|---|---|
| `type == "assistant"` whose `message.content` includes a `tool_use` block with no matching `tool_result` later in the file | `SessionPending` | A tool is waiting on permission. Covers the multi-prompt B case. |
| `type == "assistant"` plain message (no unresolved `tool_use`) | `SessionPending` | Stop-equivalent: model finished, user not yet responded. |
| `type == "user"` with timestamp > `entry.NotifiedAt` | `Resolved` | User already replied. |
| `type == "tool_result"` (model still processing — but we have no positive pending signal) | `Resolved` | Treat as session moved past the notification. |
| `type == "system"` or any other type | `Resolved` | Fall-through. |
| Empty file, parse failure, IO error | `SessionPending` (fail-safe) | Don't evict on transient errors. Stale entries are recoverable; lost ones are not. |

**Defense in depth.** The dispatcher change (drop `PostToolUse`) and the verifier change (last-line classification) both independently prevent the multi-prompt bug. Either alone would fix the common case; together they cover the asymmetric failure modes.

### Cycler: filter by reason

`ClaudeWindowService.CycleToNext` gains an optional filter:

```csharp
public void CycleToNext(WaitingReason? filter = null)
```

- `filter == WaitingReason.Permission` → only entries with `Reason == Permission`. Bound to `Ctrl+Alt+9` and the existing "Cycle Claude Now" button (renamed; see UI section).
- `filter == null` → all entries regardless of reason. Bound to `Ctrl+Alt+0` and a new "Cycle Any Waiting" button.

The cursor (`_lastFocused`) is shared across both modes. If the user cycles permissions, then cycles "any," the next press picks up where the previous one left off (consistent with how a single cursor would behave if both modes used the same list — `_lastFocused` is just the last-focused HWND, no duplication needed).

Implementation: same enumerate-cmd → match-cwd → verify → sort-by-NotifiedAt loop. The single new line in the loop is:

```csharp
if (filter is { } reason && entry.Reason != reason)
    continue;
```

placed after the verifier check.

### Hotkeys

`App.xaml.cs` replaces the single `RegisterNamedHotkey("claude-cycle", "9", …)` with two registrations:

```csharp
hotkeys.RegisterNamedHotkey("claude-cycle-perms", "9", new[] { "Control", "Alt" });
hotkeys.RegisterNamedHotkey("claude-cycle-any",   "0", new[] { "Control", "Alt" });
```

The `NamedHotkeyTriggered` handler dispatches by name:

```csharp
hotkeys.NamedHotkeyTriggered += (s, name) =>
{
    var filter = name switch
    {
        "claude-cycle-perms" => (WaitingReason?)WaitingReason.Permission,
        "claude-cycle-any"   => null,
        _                    => null  // unknown — ignore
    };
    if (name is "claude-cycle-perms" or "claude-cycle-any")
        claudeWindowService.CycleToNext(filter);
};
```

`KeyToVirtualKey` already handles `"0"` (`VK_0` = `0x30`). No change required there.

### UI affordances

#### Tray menu (`TrayIconService.cs`)

Existing single item "Cycle Claude Now" splits into two:

- "Cycle Pending Permissions" → `CyclePermissionsRequested` event → `CycleToNext(WaitingReason.Permission)`
- "Cycle Any Waiting" → `CycleAnyRequested` event → `CycleToNext(null)`

The existing `CycleClaudeRequested` event is renamed to `CyclePermissionsRequested` to keep semantics explicit. New `CycleAnyRequested` event added. New `MenuIdCycleAnyWaiting = 1005` constant.

#### Debug section on `MainPage.xaml`

The "Claude Cycler (debug)" frame gains a second button. Both buttons share the existing status label:

```xml
<Button Text="Cycle Pending Permissions" Clicked="OnCyclePermsClicked" />
<Button Text="Cycle Any Waiting"          Clicked="OnCycleAnyClicked" />
<Label  x:Name="CycleClaudeStatusLabel" />
```

`MainPage.xaml.cs` adds a parallel `OnCycleAnyClicked` handler that calls `cycler.CycleToNext(null)`. The existing handler renames to `OnCyclePermsClicked` and calls `cycler.CycleToNext(WaitingReason.Permission)`.

## Components

### New

- **`WaitingReason` enum** in `ClaudeCycler.Core.Models`.
- **Verifier classification helper** (likely a private nested type or static method on `SessionLivenessVerifier`): `LastEntryClass ClassifyLastEntry(string transcriptPath)`.

### Modified

- `ClaudeCycler.Core.Models.SessionEntry` — add `Reason` property.
- `ClaudeCycler.Core.HookDispatcher` — switch case rewrite per the dispatcher table.
- `ClaudeCycler.Core.SessionLivenessVerifier` — add tail-read + classification path.
- `MegaSchoen.Platforms.Windows.Services.ClaudeWindowService.CycleToNext` — accept `WaitingReason?` filter.
- `MegaSchoen.Platforms.Windows.Services.TrayIconService` — split menu, rename event, add new event/menu id.
- `MegaSchoen.WinUI.App.OnLaunched` — register two named hotkeys, dispatch by name, wire two tray events.
- `MegaSchoen.MainPage.xaml` + `MainPage.xaml.cs` — second button + handler.

### Unchanged

- `StateStore` — JSON serializer handles the new field automatically.
- `SettingsJsonInstaller` — already installs all five hook events.
- `ProcessResolver`, `Win32ForegroundHelper`, `GlobalHotkeyService`, `MessageWindow`.

## Data flow

```
[Notification(permission_prompt)] ─┐
                                   ├─→ HookDispatcher.Upsert(reason)
[Stop]                            ─┘
                                                                ↓
                                                            state file
                                                                ↓
[Ctrl+Alt+9]  → CycleToNext(Permission)  ┐
                                          ├─→ enumerate cmd.exe
[Ctrl+Alt+0]  → CycleToNext(null)         ┘   match cwd
                                              SessionLivenessVerifier.IsStillWaiting
                                                ↓ modtime ≤ NotifiedAt+grace → pending
                                                ↓ otherwise → tail-read JSONL → classify
                                              filter by reason if any
                                              sort by NotifiedAt
                                              advance cursor → SetForegroundWindow

[UserPromptSubmit] ─┐
[SessionEnd]        ┴─→ HookDispatcher.Delete(sessionId)

[PostToolUse] → ignored
```

## Edge cases

| Case | Behavior |
|---|---|
| Old state file with no `Reason` field | Deserializes to `Permission` (enum default 0). Correct — those were all permission entries. |
| Two permission prompts in same session, user approves first | Tool runs, `PostToolUse` fires (no-op). Verifier sees last JSONL entry is unresolved `tool_use` for second prompt → `SessionPending` → entry stays. Bug fixed. |
| `Stop` fires for a session already marked `Permission` | Upserts with `Reason=AwaitingInput`. The session was permission-pending; a `Stop` after an unresolved permission shouldn't normally occur, but if it does, treating the session as merely AwaitingInput is the safer regression — the cycle still surfaces it under `Ctrl+Alt+0`. |
| Session `Stop`s, user types nothing for an hour | Entry stays in state, surfaced by `Ctrl+Alt+0`. By design — no time cutoff per the April-24 design's stance. |
| Transcript larger than 4 KB last block | `ClassifyLastEntry` reads progressively larger chunks from the end if the last `\n` isn't found in the first 4 KB. Cap at e.g. 256 KB before giving up and returning `SessionPending` (fail-safe). |
| Concurrent transcript write while reading | Open with `FileShare.ReadWrite | FileShare.Delete`. If the read returns a partial line, parse fails → fail-safe `SessionPending`. |
| `Ctrl+Alt+0` registration refused by Windows | `RegisterHotKey` returned success for `Ctrl+Alt+0` during the April-24 spike. If a future Windows build refuses, log and surface via the existing hotkey-config path. |
| User has only AwaitingInput sessions, presses `Ctrl+Alt+9` | "No Claude windows waiting" tray notification (existing path; the candidates list is empty after filtering). |

## Testing

### Unit (`ClaudeCycler.Core.Tests`)

`HookDispatcherTests`:

- `Stop` upserts with `Reason=AwaitingInput`.
- `Stop` after `Notification(permission_prompt)` overwrites Reason to AwaitingInput (last-write-wins).
- `Notification(permission_prompt)` upserts with `Reason=Permission`.
- `UserPromptSubmit` deletes regardless of reason.
- `SessionEnd` deletes regardless of reason.
- `PostToolUse` is a no-op (state unchanged).

`SessionLivenessVerifierTests` (extended):

- Existing modtime tests still pass.
- Modtime > threshold + last entry is unresolved `tool_use` → pending.
- Modtime > threshold + last entry is `user` with later timestamp → resolved.
- Modtime > threshold + last entry is `tool_result` → resolved.
- Modtime > threshold + last entry is `system` → resolved.
- Empty transcript → resolved (fast path: file empty, treated like missing).
- Last-line JSON parse failure → pending (fail-safe).
- Tail spans multiple chunk reads (transcript with one giant final line) → pending or resolved based on actual classification.

`SettingsJsonInstallerTests`: no new tests; behavior unchanged.

### Integration

- Bridge stdin replay: `Notification(permission_prompt)` → `Stop` (in same session). Assert state shows AwaitingInput after the second event (last-write-wins).
- `CycleToNext(Permission)` with two entries (one Permission, one AwaitingInput) returns only the Permission window.
- `CycleToNext(null)` with the same state returns both, sorted by NotifiedAt.

### Manual smoke test

1. Rebuild and relaunch.
2. Open three cmd.exe Claude sessions in different cwds (A, B, C).
3. In session A: trigger a permission prompt.
4. In session B: send a prompt, let Claude respond, then idle (Stop fires).
5. Press `Ctrl+Alt+9` — cycles only A.
6. Press `Ctrl+Alt+0` — cycles A and B.
7. In session A: trigger two parallel `Bash` calls so two permission prompts queue. Approve the first; before answering the second, press `Ctrl+Alt+9`. Session A should still appear (verifier holds it via tool_use classification).
8. Type input in session B and submit. Press `Ctrl+Alt+0` — only A appears.
9. Confirm both new tray menu items work and the new "Cycle Any Waiting" button on MainPage works.

## Risks & mitigations

- **JSONL schema drift.** If Claude Code changes the entry shape, `ClassifyLastEntry` may misclassify. Fail-safe returns `SessionPending`, so the worst case is stale entries (recoverable via "Clear Needy Sessions"), not lost ones.
- **Tail-read perf.** Each `CycleToNext` call reads ~4 KB per active session. With <20 sessions this is microseconds. Re-evaluate if profile shows it.
- **Cursor confusion.** A single `_lastFocused` shared across both modes can land on a window that's not in the current filter, making `FindIndex` return -1 and effectively restarting the cycle. Acceptable — same behavior as today when a previously-focused window leaves the candidate set.
- **`Ctrl+Alt+0` collision with future Windows shortcut.** Same mitigation as the April-24 design's `Ctrl+Alt+Tab` handling: log registration failure and surface via hotkey UI.

## Out of scope

- Re-emitting `Notification` from the bridge to refresh state.
- Per-prompt tracking inside a single session.
- Cross-platform window enumeration.
- Non-cmd.exe terminals (Windows Terminal, ConEmu, etc.).
- A dedicated GUI tab listing live sessions.
