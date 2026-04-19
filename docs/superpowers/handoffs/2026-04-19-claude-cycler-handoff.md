# Claude Window Cycler — Handoff (2026-04-19)

## TL;DR

The feature is **functionally complete and working** end-to-end, with **one open issue**: the global hotkey doesn't fire when MegaSchoen runs standalone (launched from taskbar), but it does fire under the VS debugger. The same codepath works reliably from the tray-menu "Cycle Claude Now" item and from the debug button on MainPage. So it's not a code-logic bug in the cycler — something outside the process is eating the specific hotkey combo on this machine when a debugger isn't attached.

## What's working

- **Hook bridge** (`ClaudeHookBridge.exe`) receives `Notification` / `UserPromptSubmit` / `Stop` payloads from Claude Code, filters `permission_prompt` notifications, and maintains `%LOCALAPPDATA%\MegaSchoen\needy-sessions.json` via atomic writes.
- **Inspection CLI** — `status`, `logs`, `check`, `resolve` all work and are valuable for diagnosing.
- **Tray-menu installer** — right-click tray → **"Install Claude Hooks"** writes three entries to `~/.claude/settings.json` (forward-slash-normalized paths so bash doesn't eat backslashes).
- **Process resolution** — `ProcessResolver.EnumerateCmdExeWindows()` uses `Process.GetProcessesByName("cmd")` (dropped WMI — `System.Management` throws `PlatformNotSupportedException` in MAUI) and walks `GetAncestor(hwnd, GA_ROOTOWNER)` to map each ConPTY `PseudoConsoleWindow` up to the real Windows Terminal / conhost / OpenConsole host window.
- **Zombie pruning** — removed the 30-minute stale cutoff. `CycleToNext` now trusts all state entries and deletes any whose cwd doesn't match a live cmd.exe window.
- **Cycle behaviour** — `Win32ForegroundHelper.BringToFront` uses the three-way `AttachThreadInput` workaround; `SetForegroundWindow` returns `True` consistently, window actually comes forward.
- **Tray menu "Cycle Claude Now"** — reliable trigger. Use this for now.
- **MainPage "Cycle Claude Now" debug button** — same trigger path, works as confirmation.

## The open issue: hotkey interception

### Symptom

`GlobalHotkeyService.RegisterNamedHotkey("claude-cycle", ...)` returns `true`. `OnHotkeyPressed` is logged for display-profile hotkeys (e.g. `id=10 profileKnown=True` for `Ctrl+Alt+2`) but **never** logs for the named `claude-cycle` hotkey when MegaSchoen is launched from the taskbar.

Under the VS debugger (F5), the same named hotkey delivers WM_HOTKEY reliably — breakpoints fire, log entries appear, window comes forward.

### Combos we tried (all blocked standalone, all work under debugger)

- `Ctrl+Alt+Shift+C`
- `Ctrl+Alt+F12`
- `Ctrl+Alt+0`
- `Ctrl+Alt+Shift+J`
- `Ctrl+Alt+9`

### Combos that work standalone

- `Ctrl+Alt+2` (display profile hotkey — different registration path? Same class, same `RegisterHotKey` call, just registered earlier in `RefreshFromProfiles`)

### Things already ruled out

- **Not a RegisterHotKey failure**: registration returns `true`, logged on every launch.
- **Not a message-pump failure**: `WM_HOTKEY` for `Ctrl+Alt+2` arrives at our WndProc standalone. Tray icon messages (`WM_TRAYICON`) also arrive fine.
- **Not MegaSchoen's own `KeyCaptureService`**: that `WH_KEYBOARD_LL` hook is only installed during interactive "Set Hotkey" capture, not on by default.
- **Not a foreground-permission issue**: `SetForegroundWindow` path works when the cycle is invoked from the tray menu or the MainPage button. It's the hotkey delivery itself that fails, not what happens after.
- **Not MAUI thread affinity breaking Logger/MessageBox**: we added `MessageBox` via Win32 P/Invoke inside the handler; that doesn't show either when the hotkey is pressed standalone. Combined with missing log entries, this says the handler simply isn't being invoked.

### Remaining hypothesis

A low-level keyboard hook in another app (commonly: NVIDIA GeForce Experience / ShadowPlay, PowerToys Keyboard Manager, Logitech G HUB, Corsair iCUE, an AutoHotkey script) is intercepting these combos via `SetWindowsHookEx(WH_KEYBOARD_LL)` and consuming them before Windows dispatches `WM_HOTKEY`. Such hooks commonly disable themselves when a debugger is attached — which matches the observed behaviour exactly.

References:
- [microsoft/microsoft-ui-xaml#5815 — Question: how to handle WM_HOTKEY message?](https://github.com/microsoft/microsoft-ui-xaml/issues/5815)
- [Global HotKeys for Windows Applications (Lost in Details)](https://lostindetails.com/articles/Global-HotKeys-for-Windows-Applications)
- [Simon Mourier — Global keyboard accelerator in WinUI 3](https://www.simonmourier.com/blog/How-to-make-a-global-keyboard-accelerator-hotkey-for-a-button/)

### Suggested next steps (pick one)

1. **Identify the interceptor.** User should check Task Manager → Details for: `NVIDIA Share.exe`, `nvcontainer.exe`, `PowerToys.exe`, `AutoHotkey*.exe`, `lghub.exe`, `iCUE.exe`. Close each one in turn and retry Ctrl+Alt+F12 until the hotkey fires.
2. **Switch to our own `WH_KEYBOARD_LL` hook.** Install it LAST so it runs FIRST in the chain. Combos would go through our hook first and we'd consume them before the interceptor sees them. Reliable workaround but intrusive — changes how the feature is implemented. See `KeyCaptureService.cs` for a working LL-hook template in this repo.
3. **Dedicated message-pump thread** for `MessageWindow`. Not expected to help based on the data (some hotkeys arrive fine via the current pump), but it's a commonly-recommended WinUI 3 workaround that could move the hotkey off the WinUI dispatcher thread.

Personally I'd try (1) first — cheap, and if found, user can choose whether to live with the conflict or switch combo.

## Diagnostic tooling that should stay

- **`%LOCALAPPDATA%\MegaSchoen\hook-bridge.log`** — single ground truth for "is the handler actually firing". Tail it during testing.
- **`ClaudeHookBridge.exe status` / `resolve`** — verifies hook state and HWND resolution without touching MegaSchoen.
- **MainPage debug button "Cycle Claude Now"** — quick A/B test: if the button works and the hotkey doesn't, the bug is in hotkey delivery, not cycle logic.
- **Tray menu "Cycle Claude Now"** — a user-facing reliable trigger. Keep this as a supported v1 path even once the hotkey works.

## Files modified since the last clean commit (pending this handoff commit)

Core library changes:
- `ClaudeCycler.Core/ProcessResolver.cs` — `Process.GetProcessesByName` (not WMI), `GetAncestor(GA_ROOTOWNER)` for ConPTY unwrapping
- `ClaudeCycler.Core/Interop/User32.cs` — `GetAncestor`, `GA_ROOT`, `GA_ROOTOWNER`
- `ClaudeCycler.Core/StateStore.cs` — `ReadFresh` removed
- `ClaudeCycler.Core/ClaudeCycler.Core.csproj` — `System.Management` PackageReference removed
- `ClaudeCycler.Core.Tests/StateStoreTests.cs` — `ReadFresh_OmitsEntriesOlderThanCutoff` test removed

Bridge changes:
- `ClaudeHookBridge/ClaudeHookBridge.csproj` — `System.Management` PackageReference removed
- `ClaudeHookBridge/Commands/StatusCommand.cs` — stale cutoff flag removed
- `ClaudeHookBridge/Commands/ResolveCommand.cs` — uses `Read()` instead of `ReadFresh(...)`

MegaSchoen integration changes:
- `MegaSchoen/MegaSchoen.csproj` — `System.Management` direct reference removed
- `MegaSchoen/Platforms/Windows/App.xaml.cs` — hotkey currently `Ctrl+Alt+9`; handler adds `MessageBox` diagnostic, launch-time `Logger.Log`, `try/catch`; tray `CycleClaudeRequested` wired
- `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs` — zombie pruning, extensive `Logger.Log` diagnostic trace
- `MegaSchoen/Platforms/Windows/Services/GlobalHotkeyService.cs` — `OnHotkeyPressed` logs every invocation
- `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs` — added "Cycle Claude Now" menu item + `CycleClaudeRequested` event
- `MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs` — three-way AttachThreadInput pattern + BringWindowToTop + SetFocus
- `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs` — `IsIconic`, `BringWindowToTop`, `SetFocus`, `keybd_event`, `MessageBoxW`, constants
- `MegaSchoen/MainPage.xaml` + `MainPage.xaml.cs` — debug "Cycle Claude Now" button

## Not tested / not done

- Test suite still green (27 tests pass); no new tests for the zombie-pruning path in `ClaudeWindowService`.
- The MessageBox / launch-time Logger / diagnostic logging are all intentionally left in for now. Strip them once hotkey delivery is understood.
- `cwd` collision (multiple cmd.exe windows at the same cwd) cycles through all of them — spec notes this as accepted v1 behaviour but if we want to tighten, require the cmd.exe to have a `claude.exe` descendant before surfacing it.

## How to resume

1. `git log --oneline` — all the story is here.
2. Read this handoff.
3. Tail `%LOCALAPPDATA%\MegaSchoen\hook-bridge.log` in one window, launch MegaSchoen in another. Press the hotkey. If `OnHotkeyPressed` line appears → the pump delivered WM_HOTKEY → the bug is elsewhere. If it doesn't → the interceptor is still eating it; go hunt the app running the LL keyboard hook, or implement option (2) or (3) above.
