# Spec: Active Claude Sessions Dashboard

**Date:** 2026-05-09
**Origin handoff:** `docs/superpowers/handoffs/2026-05-09-active-sessions-dashboard.md`
**Status:** Approved design, ready for implementation plan

## Goal

A live dashboard of every active Claude Code session running on the local machine, with at-a-glance state badges (PendingPermission / AwaitingInput / Working / Idle / Unknown) and click-to-focus. Surfaced two ways:

1. A new **Claude Sessions** page in MegaSchoen's MAUI Shell, sibling to Display Manager.
2. A new **`ClaudeSessionsCLI`** command-line tool with `list` (one-shot or live-refreshing table) and `focus` verbs.

## Scope decisions

| Decision | Choice | Rationale |
|---|---|---|
| Platform | Windows-only v1; interfaces designed for cross-platform | Existing `ProcessResolver` is Win32; macOS/Linux impls deferred. Pull seams (`IClaudeProcessLocator`, `IClaudeWindowFocuser`) so future ports drop in cleanly. |
| Hosting | Restructure MAUI Shell to flyout with two entries: Display Manager, Claude Sessions | Cleaner long-term than growing MainPage. Existing `MainPage` becomes `DisplayManagerPage`. |
| Active definition | Only sessions with a running cmd.exe + matching cwd | Strict live-signal. Sessions with no open window are invisible; the cycler still surfaces them via tray. |
| Subagents | Rolled up under parent. Card shows count + worst-state badge. Tap to expand. | Matches handoff guidance; one card per parent for scan-at-a-glance. |
| State refresh | Event-driven via `FileSystemWatcher` — no polling | Cross-process producers (ClaudeHookBridge writes `needy-sessions.json`; Claude Code itself writes transcripts). Watchers reflect actual change events. |
| CLI shape | Single binary, three modes (default human / `--json` / `--json-stream`) per `idioms_cli_dashboard.md` | Aligns with the user's existing Python `cli_dashboard.py` conventions. Spectre.Console for human mode. |

## Project layout

### Rename

`ClaudeCycler.Core` → **`Claude.Core`** (matches the `DisplayManager.Core` `.Core` suffix idiom). Namespace `ClaudeCycler.Core.*` → `Claude.Core.*`. Test project `ClaudeCycler.Core.Tests` → `Claude.Core.Tests`. Three consumers update their `ProjectReference`: `MegaSchoen.csproj`, `ClaudeHookBridge.csproj`, `Claude.Core.Tests.csproj`. `git mv` for the directory so history follows.

### New projects

- **`ClaudeSessionsCLI/`** — sibling to `DisplayManagerCLI`. `net10.0-windows` (explicit Windows TFM). `OutputType=Exe`. `PackageReference: Spectre.Console`. `ProjectReference: Claude.Core`. AnyCPU.

### No new test projects

New types in `Claude.Core` get tests in the existing `Claude.Core.Tests`.

## Domain model (in `Claude.Core`)

```csharp
public enum SessionState
{
    PendingPermission, // in StateStore with Reason=Permission
    AwaitingInput,     // in StateStore with Reason=AwaitingInput
    Working,           // last transcript line type=assistant AND not in StateStore
    Idle,              // last transcript line type=user/tool_result/system AND not in StateStore
    Unknown            // transcript missing or unreadable
}

public readonly record struct WindowToken { internal IntPtr Handle { get; init; } }

public sealed record SessionSnapshot(
    string SessionId,
    string Cwd,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State,
    string? PendingMessage,            // permission prompt text when PendingPermission
    WindowToken Window,
    string? WindowTitle,
    IReadOnlyList<SubagentSnapshot> Subagents)
{
    public SessionState RollupState =>
        Subagents.Count == 0
            ? State
            : (SessionState)Math.Min((int)State, Subagents.Min(s => (int)s.State));
}

public sealed record SubagentSnapshot(
    string AgentId,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State);
```

The enum is ordered by attention priority — lower ordinal = more attention-needed. `RollupState` is `Math.Min` over parent + subagents.

### State classification

**`SessionStateClassifier`** — pure function `(SessionEntry?, transcriptPath) → SessionState`:

- StateStore hit → `PendingPermission` / `AwaitingInput` from `Reason`.
- Otherwise tail-read transcript (reuse `SessionLivenessVerifier.ClassifyLastEntry` — promote from `private` to `internal` or expose a sibling public method):
  - last line `type:"assistant"` → `Working`
  - last line `type:"user"` / `tool_result` / `system` → `Idle`
  - tail-read failure → `Unknown`

**No time gates in classification.** The discriminator between Working and AwaitingInput is StateStore presence, not mtime — `Stop` hook upserts AwaitingInput entries (`HookDispatcher.cs:37`). The pre-existing 5-second grace inside `SessionLivenessVerifier.IsStillWaiting` is a tail-read fast-path optimization, not state classification — it stays.

### Active session enumeration

```csharp
public sealed class ActiveSessionEnumerator
{
    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store);
    public IReadOnlyList<SessionSnapshot> Enumerate();
}
```

Algorithm:

1. `var windows = locator.EnumerateWindows();` (Windows impl wraps existing `ProcessResolver.EnumerateCmdExeWindows`).
2. `var stateFile = store.Read();`
3. For each window with a non-null `cwd`:
   - Slug = `cwd.Replace(':','-').Replace('\\','-').Replace('/','-')` (consecutive separators give double-hyphens; matches Claude Code's encoding).
   - Look up `~/.claude/projects/<slug>/`. If missing, skip.
   - Glob top-level `*.jsonl`. Most-recently-modified wins per cwd.
   - Sanity-check against slug collisions (e.g., `C:\foo\bar` and `C:\foo-bar` both encode to `C--foo-bar`): read the first line of the JSONL; if it has a `cwd` field, verify match against window cwd. If the first line has no `cwd` field, accept the slug match without verification.
   - Subagents: glob `<session-id>/subagents/agent-*.jsonl`.
4. Classify each session via `SessionStateClassifier` (joining StateStore lookups by session-id).
5. Sort: state ordinal ascending, then `LastActivityUtc` descending within each bucket.

Known limitation: multiple windows in the same cwd → both match the same JSONL, focus picks whichever the focuser returns first. Rare; documented.

### Platform abstractions

```csharp
public interface IClaudeProcessLocator
{
    IReadOnlyList<ClaudeWindow> EnumerateWindows();
}

public readonly record struct ClaudeWindow(uint ProcessId, WindowToken Window, string Title, string? WorkingDirectory);

public interface IClaudeWindowFocuser
{
    bool BringToFront(WindowToken window);
}
```

Windows impls live in `Claude.Core` (already mostly-Windows project; native interop already inside it):

- `WindowsClaudeProcessLocator` → wraps existing `ProcessResolver.EnumerateCmdExeWindows`.
- `WindowsClaudeWindowFocuser` → wraps existing `Win32ForegroundHelper.BringToFront` (currently in `MegaSchoen/Platforms/Windows/Services/`; move to `Claude.Core/Interop/` so the CLI can use it without depending on MAUI).

## CLI surface (`ClaudeSessionsCLI`)

Hand-rolled `switch` on `args[0]` matching `DisplayManagerCLI/Program.cs` style.

### Verbs

```
ClaudeSessionsCLI list  [--json] [--json-stream] [--interval 1.5]
ClaudeSessionsCLI focus <session-id-prefix>
```

### `list` modes

| Mode | Flag | Behavior |
|---|---|---|
| Human (default) | _none_ | `Spectre.Console.AnsiConsole.Live(table)`. Refresh on watcher events (debounced 250ms). Ctrl-C exits cleanly. If `Console.IsOutputRedirected`, render once and exit (don't pollute pipes with ANSI). |
| One-shot JSON | `--json` | Pretty JSON array of `SessionSnapshot`. Single document. Exit 0. |
| Streaming JSON | `--json-stream` | NDJSON. Identical-shape `SessionSnapshot[]` document per refresh tick. `Console.Out.FlushAsync` after each line. Ctrl-C exits 0. **No `type:"snapshot"` vs `type:"change"` distinction** — same shape every tick, consumers diff if they care. |

`--interval` default 1.5s; only meaningful for `--json-stream` (and acts as a max-rate cap on human-mode refreshes — watcher-driven refresh still drives most updates).

All non-data output (warnings, "no Claude windows") goes to **stderr** so `--json*` stdout stays parseable.

### `focus <session-id-prefix>`

Resolve `prefix` to a unique session-id stem from `Enumerate()`. If unique → `IClaudeWindowFocuser.BringToFront(snapshot.Window)`, exit 0. If ambiguous or not found, error to stderr, exit 1. Other errors exit 2.

### Build wiring

Add `ClaudeSessionsCLI` to `MegaSchoen.sln`. Solution-config mappings AnyCPU/x64/x86 → `Debug|x64` (matches `DisplayManagerCLI`).

## MAUI Shell restructure

### File renames

- `MainPage.xaml` / `.xaml.cs` → `DisplayManagerPage.xaml` / `.xaml.cs`
- `MainPageViewModel.cs` → `DisplayManagerPageViewModel.cs`

### `AppShell.xaml`

Replace single `ShellContent` with two `FlyoutItem`s:

```xml
<Shell ...>
    <FlyoutItem Title="Display Manager" Icon="display.png">
        <ShellContent ContentTemplate="{DataTemplate local:DisplayManagerPage}" Route="displays" />
    </FlyoutItem>
    <FlyoutItem Title="Claude Sessions" Icon="sessions.png">
        <ShellContent ContentTemplate="{DataTemplate local:SessionsPage}" Route="sessions" />
    </FlyoutItem>
</Shell>
```

### New: `SessionsPage` + `SessionsPageViewModel`

`SessionsPageViewModel` is `INotifyPropertyChanged` + `IDisposable`. Holds `ObservableCollection<SessionCardViewModel>`. Constructor injects `ActiveSessionEnumerator`, `IClaudeWindowFocuser`, `IDispatcher` (MAUI's UI-thread marshaller).

### Refresh mechanism

Two `FileSystemWatcher` instances, both funneling into one debounced channel:

1. **`StateStoreWatcher`** — `%LOCALAPPDATA%\MegaSchoen\needy-sessions.json` (file-level, `Changed` + `Created`).
2. **`TranscriptsWatcher`** — `~/.claude/projects/` recursive, filter `*.jsonl`, `Changed` + `Created`.

```csharp
readonly Channel<Unit> _refreshSignal =
    Channel.CreateBounded<Unit>(new BoundedChannelOptions(1) { FullMode = DropWrite });

void OnAnyEvent(object? _, FileSystemEventArgs __) => _refreshSignal.Writer.TryWrite(default);

async Task ConsumeAsync(CancellationToken ct)
{
    await foreach (var _ in _refreshSignal.Reader.ReadAllAsync(ct))
    {
        await Task.Delay(250, ct);                              // coalesce burst
        while (_refreshSignal.Reader.TryRead(out _)) { }         // drain
        var snapshots = _enumerator.Enumerate();
        await _dispatcher.DispatchAsync(() => UpdateUi(snapshots));
    }
}
```

The bounded channel + drain pattern coalesces event storms into one re-enumeration without dropping the most recent signal. No timer, no polling.

Initial load on `OnAppearing` so the page isn't blank for 250ms. Disposal on `OnDisappearing` cancels the loop and disposes both watchers.

**Acknowledged caveat:** `FileSystemWatcher` recursive on `~/.claude/projects/` tracks every transcript-line append across every project the user has ever touched. Volume is bounded (Claude writes infrequently — once per turn or tool result), but flag for future perf if it bites. No fallback periodic re-enumeration in v1.

### Card layout (`SessionsPage.xaml`)

`CollectionView` of cards, sorted by `RollupState` ordinal then `LastActivityUtc` descending. Each card:

- **Top row:** colored state badge | cwd (truncated middle) | `LastActivityRelative` ("3s ago") | Focus button
- **Expanded row** (when `IsExpanded`): per-subagent mini-rows with their own state badges
- **Empty state:** "No active Claude sessions" centered

Tap card to toggle `IsExpanded`. Click Focus button to invoke `IClaudeWindowFocuser.BringToFront`.

### Cycler debug section moves here

The "Claude Cycler (debug)" section currently in `MainPage.xaml:217-238` (Cycle Pending Permissions / Cycle Any Waiting buttons) moves to the bottom of `SessionsPage`. The same handlers (`OnCyclePermsClicked`, `OnCycleAnyWaitingClicked`) come along.

### DI registrations (`MauiProgram.cs`)

```csharp
#if WINDOWS
builder.Services.AddSingleton<IClaudeProcessLocator, WindowsClaudeProcessLocator>();
builder.Services.AddSingleton<IClaudeWindowFocuser, WindowsClaudeWindowFocuser>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<ActiveSessionEnumerator>();
builder.Services.AddTransient<SessionsPageViewModel>();
builder.Services.AddTransient<SessionsPage>();
builder.Services.AddTransient<DisplayManagerPage>();
#endif
```

On non-Windows targets the Sessions flyout entry shows a "Windows-only" placeholder page (clearer than a missing tab).

## Testing

In existing `Claude.Core.Tests` (renamed):

1. **Pure logic — full coverage.** `SessionStateClassifier`, slug-encoding helper, `SessionSnapshot.RollupState`, sort comparer.
2. **Integration — happy path + edges.** `ActiveSessionEnumerator` against a temp-dir fake `~/.claude/projects/` tree with a fake `IClaudeProcessLocator`. Cover: no windows; window with no slug dir; freshest-JSONL wins; subagent rollup; cwd-mismatch sanity-check skip.
3. **CLI smoke** — one test per verb. Spawn the built exe, parse `--json` output, assert exit codes.

Not tested:
- Windows process locator / focuser implementations (testing Windows itself; covered by manual smoke + existing cycler tests).
- MAUI ViewModel against a real UI thread. `MegaSchoen.UITests` adds one screenshot of `SessionsPage` with mock data.

## Out of scope for v1

- Cross-platform impls (interfaces exist; macOS/Linux locators/focusers TODO).
- Cross-machine view (llamabox sessions). Defer.
- Sessions with no open window — the cycler tray flow already covers this.
- Spawning new sessions from the dashboard.
- Cost / token usage per session — probably v2.
- "Errored" badge — no clean detection signal yet.
- FileSystemWatcher reliability fallback (60s safety re-enumerate). Add only if missed events are observed.

## Acceptance criteria

1. `ClaudeSessionsCLI list --json` from a terminal where Claude Code is running prints a JSON array including that session with the right state.
2. `ClaudeSessionsCLI list` (human) shows a refreshing table that updates within ~100ms when a hook fires in another window.
3. `ClaudeSessionsCLI focus <prefix>` brings the matching window to foreground.
4. MegaSchoen has two flyout entries: Display Manager + Claude Sessions. Sessions page renders cards, sorted by attention-need, focuses on click.
5. All existing cycler hotkeys / tray / debug-button functionality unchanged.
6. `Claude.Core.Tests` green; coverage gate satisfied.
