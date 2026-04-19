# Claude Window Cycler — Design

**Date:** 2026-04-18
**Status:** Approved for planning
**Scope:** Feature addition to MegaSchoen (MAUI Windows app)

## Problem

When multiple Claude Code sessions are running across many `cmd.exe` windows, there is no way to tell from outside a window whether a given session is **waiting for user input** (permission prompt) versus **still thinking**. All busy-but-idle Claude windows look the same from the taskbar — green icon, same title. This forces hunting: alt-tab through every cmd.exe window to find the ones that need attention, while trying to do other work.

A previous attempt that inferred session state by reading `~/.claude/projects/…` transcript artifacts failed — the state was too noisy to classify reliably.

## Goal

One global hotkey that brings the *next* Claude session currently waiting on a permission prompt to the foreground. Cycling presses advance through all currently-waiting sessions. Sessions that are still mid-work, and sessions that are idle waiting for the next user prompt, are both skipped.

## Non-goals (v1)

- Cross-platform window focusing. Architecture leaves room; implementation is Windows-only.
- Support for terminals other than `cmd.exe` (PowerShell, Windows Terminal, WSL).
- Any UI beyond the hotkey and a soft tray notification.
- Auto-approving prompts. The tool surfaces; the user decides.
- Distinguishing permission-prompt state from "idle waiting for next prompt" state in any way beyond filtering the latter out.

## Core design decision: signal source

The signal that drives the tool is **Claude Code's own `Notification` hook**, not inferred terminal state. Two adjacent hooks (`UserPromptSubmit`, `Stop`) clear the entry when the session resumes or finishes. This is authoritative — Claude is telling us its state — and immune to UI pattern drift.

Filtering: the `Notification` hook fires with a `notification_type` field; v1 matches only `permission_prompt`. `idle_prompt`, `auth_success`, and `elicitation_dialog` are ignored.

## Architecture

Three pieces:

### 1. `ClaudeHookBridge.exe` — hook handler + inspection CLI

New .NET 10 console project in the MegaSchoen solution. Two modes, disambiguated by argv:

**Hook mode (no args):** reads hook JSON from stdin, dispatches on `hook_event_name`, updates the state file. Always exits 0; internal errors go to a log file, never to Claude.

Dispatch:

- `Notification` + `notification_type == "permission_prompt"` → upsert session entry
- `UserPromptSubmit` → delete entry for `session_id`
- `Stop` → delete entry for `session_id`
- Anything else → no-op

**Inspection mode (subcommands):** developer / user tool for checking the internals without a GUI.

- `ClaudeHookBridge.exe status` — prints the current needy-sessions state file, pretty-printed, with stale entries flagged.
- `ClaudeHookBridge.exe resolve` — runs the same cwd→HWND pipeline `ClaudeWindowService` uses, but prints each resolution (sessionId, cwd, cmd.exe pid, HWND, window title) rather than focusing. Primary debug tool when the hotkey "doesn't work."
- `ClaudeHookBridge.exe check` — reads `~/.claude/settings.json`, reports which of the three hooks are installed, at what path, and whether that matches the current `ClaudeHookBridge.exe` location.
- `ClaudeHookBridge.exe logs [-f]` — prints `hook-bridge.log`; `-f` tails.

Logs go to `%LOCALAPPDATA%\MegaSchoen\hook-bridge.log`.

### 2. State file

Path: `%LOCALAPPDATA%\MegaSchoen\needy-sessions.json`

Schema:

```json
{
  "version": 1,
  "sessions": {
    "<session_id>": {
      "cwd": "C:\\Users\\mtsch\\source\\repos\\Foo",
      "notifiedAt": "2026-04-18T14:23:00Z",
      "message": "Claude needs your permission to use Bash"
    }
  }
}
```

Writes are atomic: serialize to `needy-sessions.json.tmp`, then `File.Move(..., overwrite: true)`. On read, a corrupt file is treated as empty and overwritten on next update.

### 3. `ClaudeWindowService` — MegaSchoen side

New class in `MegaSchoen/Platforms/Windows/Services/`. Two methods:

- `List<ClaudeWindow> FindNeedyWindows()`
  1. Read state file; filter entries with `notifiedAt` older than 30 minutes.
  2. Enumerate `cmd.exe` processes via WMI (`Win32_Process`).
  3. For each cmd.exe, read its current working directory from the PEB (via `NtQueryInformationProcess(ProcessBasicInformation)`).
  4. Match cwd against state-file entries.
  5. Map matching cmd.exe pids to top-level HWNDs by `EnumWindows` + `GetWindowThreadProcessId`.
  6. Return ordered list of `(sessionId, HWND, notifiedAt)`.
- `void CycleToNext()`
  1. Call `FindNeedyWindows`, order by `notifiedAt` ascending.
  2. Using an in-memory "last focused HWND" cursor, advance to the next entry; wrap at end.
  3. `SetForegroundWindow(hwnd)` using the existing MegaSchoen focus-helper (whichever `AttachThreadInput` / `AllowSetForegroundWindow` trick already exists in the codebase).
  4. If the list is empty: soft tray notification ("No Claude windows waiting"). No focus change.

Hotkey registration goes through the existing `GlobalHotkeyService` and follows whatever hotkey-config UI pattern MegaSchoen already has for display-profile hotkeys.

### 4. Hook installer

A one-time setup action (menu item or settings button) that merges three entries into `~/.claude/settings.json`:

```jsonc
{
  "hooks": {
    "Notification":      [{ "command": "<path>\\ClaudeHookBridge.exe" }],
    "UserPromptSubmit":  [{ "command": "<path>\\ClaudeHookBridge.exe" }],
    "Stop":              [{ "command": "<path>\\ClaudeHookBridge.exe" }]
  }
}
```

Behavior:

- Idempotent — re-running with the same path is a no-op.
- Creates `settings.json.bak` before modifying.
- Preserves existing unrelated hooks.
- If an existing entry for one of these events points at a *different* path, prompt the user before overwriting.

## Data flow

```
Claude fires Notification hook
    ↓
ClaudeHookBridge.exe writes needy-sessions.json (atomic)
    ↓
 [user presses hotkey]
    ↓
MegaSchoen.ClaudeWindowService.CycleToNext()
  ↓ read state file
  ↓ WMI enumerate cmd.exe, read cwd from PEB, match
  ↓ map pid → HWND, advance cursor, SetForegroundWindow
    ↓
 [user approves in the terminal, or types a new prompt]
    ↓
Claude fires UserPromptSubmit or Stop
    ↓
ClaudeHookBridge.exe deletes session entry
```

## Edge cases

| Case | Behavior |
|---|---|
| Hook bridge crashes | Caught, logged, exit 0. Claude never blocked. |
| State file missing / corrupt | Treat as empty. Next write rebuilds. |
| Session crashed without firing `Stop` | 30-minute `notifiedAt` cutoff filters stale entries. |
| Two cmd.exe windows with same cwd | Cycle surfaces both; ordering by `notifiedAt` is deterministic. |
| Claude in PowerShell / WSL / Windows Terminal | v1 does not detect. Documented limitation. |
| User closes cmd.exe while state says "needy" | WMI enumeration won't find a live pid; entry is naturally absent from results. |
| No needy windows on hotkey press | Soft tray notification; no focus change. |
| Hooks not yet installed | State file doesn't exist → empty result. First hotkey press prompts the user to run the installer. |
| Existing unrelated hooks in `settings.json` | Installer merges non-destructively. Existing same-event entry at a different path prompts for confirmation. |
| Rapid repeated notifications for same session | Upsert by `session_id`; later write just refreshes `notifiedAt`. |

## Testing

**Unit tests** (new `ClaudeHookBridge.Tests` project, or inside `MegaSchoen.UITests`):

- Hook payload parser: all four event types, with and without `notification_type`, malformed JSON.
- State file: upsert, delete, corruption recovery, atomic-write verification (simulate crash between temp-write and rename).
- Stale-cutoff filtering.
- cwd normalization (case-insensitive, trailing-slash tolerant).

**Integration test:**

- Spawn a stub process that invokes `ClaudeHookBridge.exe` with crafted stdin payloads simulating a permission prompt followed by a `Stop`. Assert state file transitions: absent → present → absent.
- Mock WMI / process-tree access behind an interface so `ClaudeWindowService` can be tested without real Claude processes running.

**Manual smoke test:**

1. Install hooks. Run `ClaudeHookBridge.exe check`; verify the three hooks point at the current binary.
2. Open three cmd.exe Claude sessions in different directories.
3. Trigger a permission prompt in one. Run `ClaudeHookBridge.exe status`; verify entry appears. Run `resolve`; verify cwd resolves to the correct HWND and window title.
4. Press hotkey; verify that window comes forward.
5. Approve the prompt; `status` should show no entries; press hotkey; verify "none waiting."
6. Trigger prompts in two sessions; verify hotkey cycles between both.
7. Kill a Claude session mid-wait; verify hotkey skips it cleanly and `resolve` shows the orphaned entry as unresolved.

## Open implementation questions (spike early)

1. **Reading cmd.exe's cwd from a second process.** WMI doesn't expose it. Options: `NtQueryInformationProcess(ProcessBasicInformation)` + PEB read (documented-but-technically-unsupported), or shell out to a helper that uses official debug APIs. Plan: spike `NtQueryInformationProcess` first; it's widely used and reliable in practice. Fallback: have the hook record the claude.exe PID at notification time, and match by pid — this requires assuming the (undocumented but effectively guaranteed on Windows) hook→claude parent relationship.
2. **`SetForegroundWindow` focus-steal prevention.** Reuse whatever existing pattern MegaSchoen already uses to focus its own window; if none, use `AllowSetForegroundWindow` + `AttachThreadInput` trick.
3. **Hook schema drift.** Log the full payload of every `Notification` event for the first week of use to a rotating log, so we can audit if `notification_type` values ever diverge from what docs say.

## Risks & mitigations

- **Claude Code changes the `Notification` hook schema.** Mitigation: bridge no-ops on unknown fields; state file is versioned; full-payload logging catches drift fast.
- **PEB reading breaks on a future Windows update.** Mitigation: fallback path (hook-records-pid) sketched above.
- **Users with many simultaneous Claude sessions overwhelm the state file.** Unlikely in practice; file is tiny; atomic writes mean no contention issues.

## Cross-platform note

Pieces 1, 2, and 4 (hook bridge, state file, installer) are pure .NET and portable. Piece 3 (cwd→HWND resolution and window focusing) is Windows-specific. If cross-platform support is ever wanted, the reduced-scope first step for macOS/Linux would be: show a system notification with the session's cwd and an "activate" button, rather than attempting to map to a window handle. That's additive; no architectural change required.

## Follow-ups (not in v1 scope)

- **Dedicated GUI surface inside MegaSchoen.** MegaSchoen's current UI is dominated by the display-profile switcher. The longer-term direction is a tool-suite layout (tabs / navigation) rather than one-UI-per-tool. A Claude-cycler tab would show: live list of currently-waiting sessions (session id, cwd, time waiting, the notification message), a "focus" button per row, hook install status, and the hotkey binding. The v1 state file already makes this straightforward — the GUI is purely a reader. No v1 architectural change is required to accommodate it.
- **Supporting PowerShell / Windows Terminal / WSL.** Same hook bridge, same state file; only the window-resolution step changes. Extend `ClaudeWindowService` to enumerate more shell hosts.
- **Cross-platform window focusing.** Per the cross-platform note above.
