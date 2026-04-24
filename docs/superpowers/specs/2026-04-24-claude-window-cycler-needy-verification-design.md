# Claude Window Cycler — Needy-Entry Verification & Hotkey Refinement

**Date:** 2026-04-24
**Status:** Approved for planning
**Scope:** Follow-up to [2026-04-18-claude-window-cycler-design.md](2026-04-18-claude-window-cycler-design.md). No architectural change; closes gaps discovered in real use.

## Context

The Claude Window Cycler from the April-18 design ships and runs end-to-end: hooks fire, the state file gets populated, the hotkey cycles windows. What it does not do reliably is **filter stale entries**. The state file accumulates entries whose permission prompts have been resolved, so the hotkey surfaces windows that are not actually waiting on anything. The April-18 doc proposed a 30-minute time cutoff; we are **dropping that** — the user frequently steps away for hours and wants pending permissions to remain reachable.

## Problems to fix

1. **Missed resolution events.** The dispatcher listens for `UserPromptSubmit` and `Stop`. Neither fires when a permission is granted mid-turn and Claude keeps working on subsequent tool calls, and neither fires on clean session exit.
2. **No authoritative live-state check.** Once an entry is written, nothing re-verifies it. If hooks are missed for any reason (bridge crash, abrupt kill, event we're not listening for), the entry lingers until its cmd.exe dies.
3. **Hotkey is unnatural.** `Ctrl+Alt+9` was a placeholder. Target is `Ctrl+Alt+Tab`.

## Non-goals

- Changing the core architecture (hook bridge, state file, resolver stay as in April-18).
- Any time-based expiry. Entries live until resolved.
- Parsing transcript *content*. We use file metadata only; content parsing is a documented fallback.

## Approach — two complementary mechanisms

### Approach A: broader hook coverage

Add two dispatches to `ClaudeCycler.Core.HookDispatcher`:

| Event | Action | Reasoning |
|---|---|---|
| `PostToolUse` | Delete entry for `session_id` | Tool ran successfully → the permission that gated it was granted. Closes the "approved mid-turn, turn ongoing" case. |
| `SessionEnd` | Delete entry for `session_id` | Catches clean `/exit` and window-close-while-idle. |

Existing dispatches (`Notification(permission_prompt)` → upsert, `UserPromptSubmit` / `Stop` → delete) are unchanged.

`SettingsJsonInstaller` is extended to install hooks for the two new events alongside the existing three. The installer is already idempotent, so re-running picks them up.

**Gap this leaves:** abrupt process kill (`taskkill`, BSOD, power loss). No hook fires. Covered by existing "no matching cmd.exe → delete entry" logic in `ClaudeWindowService`, and by Approach C below.

### Approach C: cycle-time liveness verification

On every `CycleToNext` call, each state-file entry is checked against its session's transcript file before being treated as needy.

**Transcript location:** `~/.claude/projects/<encoded-cwd>/<session_id>.jsonl`, where `<encoded-cwd>` is the session's cwd with `\` and `:` replaced by `-` (matching Claude Code's existing convention — verified during spike; see Open Questions).

**Check:** compare the transcript file's `LastWriteTimeUtc` against the state entry's `NotifiedAt`.

```
grace = 5 seconds                  // clock skew / flush latency buffer

if transcriptFile does not exist:
    treat as resolved (session likely archived) → delete entry, skip
else if transcriptFile.LastWriteTimeUtc > entry.NotifiedAt + grace:
    something happened in this session after the notification
    → permission was resolved or session progressed → delete entry, skip
else:
    nothing has happened since the notification → still pending → include
```

This is pure file-metadata inspection — no JSONL parsing, no UI pattern matching. If the modtime heuristic proves unreliable (e.g., Claude Code touches the transcript for reasons unrelated to turn progression), the fallback is to read the final JSONL line and classify by event type. That fallback is not implemented in v1.

### Hotkey change

- Default binding: `Ctrl+Alt+Tab` (was `Ctrl+Alt+9`).
- Registration: extend `KeyToVirtualKey` to map `"Tab"` → `VK_TAB` if not already supported.
- **Risk:** `Ctrl+Alt+Tab` is the Windows "persistent task switcher" shortcut. Empirically `RegisterHotKey` succeeds for it on Windows 11, but we must verify during the spike. If `RegisterHotKey` returns failure, fall back to prompting the user for an alternative via the existing hotkey-config UI rather than silently reverting.

## Components

### New: `ClaudeCycler.Core.SessionLivenessVerifier`

```csharp
public sealed class SessionLivenessVerifier
{
    readonly IFileSystem _files;           // abstraction for test seams
    readonly TimeSpan _grace;              // default 5s

    public SessionLivenessVerifier(IFileSystem files, TimeSpan? grace = null);

    public bool IsStillWaiting(string sessionId, SessionEntry entry);
}
```

- No dependency on `StateStore`; pure function of entry + filesystem.
- Cross-platform (transcript path is `~/.claude/projects/...` on all OSes).
- Unit-testable with a fake `IFileSystem` or a real temp directory.

### Modified: `ClaudeWindowService.CycleToNext`

Insertion point: after cwd match, before `candidates.Add`:

```csharp
if (!_verifier.IsStillWaiting(id, entry))
{
    _store.Delete(id);
    continue;   // do not count toward matchedSessionIds
}
candidates.Add((id, window, entry.NotifiedAt));
```

### Modified: `ClaudeCycler.Core.HookDispatcher`

Add two cases to the existing `switch`:

```csharp
case "PostToolUse":
case "SessionEnd":
    _store.Delete(payload.SessionId);
    break;
```

### Modified: `ClaudeCycler.Core.SettingsJsonInstaller`

Extend the list of hook events installed from `{Notification, UserPromptSubmit, Stop}` to `{Notification, UserPromptSubmit, Stop, PostToolUse, SessionEnd}`. Behavior otherwise unchanged (idempotent merge, backup, prompt on conflicting path).

### New: tray menu item "Clear needy sessions"

Manual escape hatch. Writes an empty `NeedySessionsFile` to disk. Useful during debugging and for rare real-world drift cases. No confirmation dialog (action is cheap to undo by waiting for the next notification).

### Hotkey binding change

- `App.xaml.cs` line 135: replace `RegisterNamedHotkey("claude-cycle", "9", {"Control", "Alt"})` with `"Tab"`.
- `Win32Interop.KeyToVirtualKey` (wherever it lives): add `"Tab" → VK_TAB (0x09)` if absent.

## Data flow (unchanged except for the verifier step)

```
Claude fires hook → ClaudeHookBridge.exe → state file

[hotkey press]
    ↓
CycleToNext
    ↓ read state
    ↓ enumerate cmd.exe, match by cwd
    ↓ SessionLivenessVerifier.IsStillWaiting(entry)    ← NEW
    ↓   ├─ transcript modtime ≤ notifiedAt+grace  →  include in cycle
    ↓   └─ otherwise                              →  delete entry, skip
    ↓ advance cursor, SetForegroundWindow
```

## Edge cases

| Case | Behavior |
|---|---|
| Transcript file not found | Treat as resolved, delete entry. (Either the session was archived, or the cwd-encoding assumption is wrong — spike catches the latter.) |
| Transcript modtime equals `notifiedAt` to the second | Within grace window → treated as still pending. |
| Clock skew between hook-firing and transcript-flush | Grace window (5s). If observed skew exceeds this regularly, tune or switch to fallback JSONL parsing. |
| Multiple permissions in one turn | Each `Notification` upserts the entry (refreshes `notifiedAt`); each `PostToolUse` deletes it. Last write wins. If user is waiting on permission N+1 when permission N's PostToolUse fires first, we transiently delete then immediately re-upsert on the next `Notification` — acceptable. |
| `PostToolUse` fires but permission was denied | Tool didn't run in the denial case, so `PostToolUse` doesn't fire. `Stop` fires eventually. Covered. |
| `Ctrl+Alt+Tab` registration refused by Windows | Log the failure; surface via the existing hotkey-config UI; keep app running. |
| State file purged via tray "Clear needy sessions" while hotkey is held | Next press sees empty state → "No Claude windows waiting" tray notification. Normal path. |

## Testing

**Unit (`ClaudeCycler.Core.Tests`):**

- `SessionLivenessVerifier.IsStillWaiting` — transcript missing, modtime equals `notifiedAt`, modtime before/within/after grace, grace tunable.
- `HookDispatcher` — `PostToolUse` and `SessionEnd` delete the right entry; unknown events still no-op.
- `SettingsJsonInstaller` — installing against a settings file that already has three hooks adds the two new ones without disturbing unrelated entries.

**Integration:**

- Spawn bridge with stdin payloads for `Notification(permission_prompt)` → `PostToolUse` sequence; assert state transitions absent → present → absent.
- `CycleToNext` end-to-end with a fake filesystem: one live entry with fresh transcript modtime, one stale entry with newer transcript modtime. Assert only the live one is surfaced, the stale one is deleted from state.

**Manual smoke test:**

1. Rebuild; run installer from tray; `ClaudeHookBridge.exe check` shows all five hooks installed.
2. Open three cmd.exe Claude sessions.
3. Trigger permission in session A → `status` shows one entry.
4. Approve the prompt; let Claude continue working for ~30s (still mid-turn).
5. Press hotkey → should report "no needy" (verifier sees transcript updated after notification).
6. `status` should show the entry gone.
7. Trigger permissions in sessions B and C simultaneously; cycle between them.
8. Kill session B's cmd.exe; cycle again; verify it skips cleanly.
9. Close Claude in session C cleanly with `/exit`; `status` shows the entry removed (via `SessionEnd`).
10. Verify `Ctrl+Alt+Tab` triggers the cycle.

## Open implementation questions (spike early)

1. **Transcript path encoding.** Confirm that `C:\Users\mtsch\source\repos\Foo` maps to `C--Users-mtsch-source-repos-Foo` (or whatever Claude Code actually uses). Read one live session's transcript path and verify the mapping. If the rule is more complex (e.g., lowercase, URL-escape, hash), encode it exactly.
2. **Transcript write semantics.** Confirm Claude Code touches the transcript on every turn-progression event and not just on some internal buffer flush. One-line shell test: watch `LastWriteTimeUtc` while approving a permission in a live session.
3. **`PostToolUse` payload shape.** Confirm `session_id` is on the payload for this event type. Failure mode: dispatcher logs "missing session_id" and no-ops — detectable via logs.
4. **`Ctrl+Alt+Tab` registration.** Spike: try `RegisterHotKey` for `MOD_CONTROL | MOD_ALT | VK_TAB` on Windows 11. If refused, pick an alternative (`Ctrl+Shift+Tab`? `Ctrl+Alt+Space`?) and document.

All four are cheap (under 30 minutes each) and should be done before writing implementation code for the piece that depends on them.

## Risks & mitigations

- **Claude Code changes transcript write behavior.** If a future update batches writes, modtime could lag behind actual events and we'd hold stale entries longer. Mitigation: easy fallback to JSONL-last-line parsing; no architectural change needed.
- **Transcript path format changes.** Same mitigation as April-18 design's handling of hook schema drift: log once, fix once.
- **`Ctrl+Alt+Tab` breaks on some Windows builds.** Graceful fallback via existing hotkey UI.

## Out of scope (confirming April-18 non-goals still hold)

- Cross-platform window focusing.
- Non-cmd.exe terminals.
- Auto-approving prompts.
- A dedicated GUI tab inside MegaSchoen for live session list (still a noted follow-up).
