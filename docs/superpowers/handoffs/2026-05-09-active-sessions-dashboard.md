# Handoff: Active Claude Sessions Dashboard

**Created:** 2026-05-09
**Origin:** chonkers session, conversation about whether a tool exists to show all currently-running Claude Code sessions in one place
**Goal:** Add a "live sessions" view to MegaSchoen — a central dashboard of every active Claude Code session across the machine (and eventually across machines), with at-a-glance state badges (waiting-for-input / working / idle / errored) and click-to-focus.

## Why MegaSchoen is the right starting point

This repo already has the two hardest pieces wired up:

1. **Window enumeration / cycling** — `ClaudeCycler.Core` already finds Claude Code windows and switches focus between them. The dashboard is largely a UI wrapper around this plus state badges.
2. **Session-state detection** — `ClaudeCycler.Core/SessionLivenessVerifier.cs` already classifies a transcript's last entry as `SessionPending` vs `Resolved` by tail-reading the JSONL. This is the exact primitive a "needs input / working / idle" badge needs.

The MAUI host (`MegaSchoen` project) is the natural place to host the dashboard view alongside Display Manager.

## Cross-platform requirement (the hard bit)

User explicitly wants this cross-platform. MegaSchoen is currently Windows-only because:

- `DisplayManagerNative` is a Win32 C++ DLL (CCD API) — irrelevant to the dashboard, just keep it Windows-conditional.
- `ClaudeCycler.Core/Interop/` and `ProcessResolver.cs` likely use Win32 APIs to find Claude windows / processes — **this is the actual portability blocker for the dashboard**. Audit these first; abstract behind an `IClaudeProcessLocator` interface with Windows / macOS / Linux implementations.

The transcript-reading half (`SessionLivenessVerifier`, `StateStore`, `Paths`) is already pure managed code and should port unchanged. Sessions live at `~/.claude/projects/<encoded-cwd>/<session-id>.jsonl` on every platform — same path layout, just a different home dir.

MAUI targets Windows + macOS today; iOS/Android are non-goals. Linux GUI is not first-class in MAUI — for Linux either ship a CLI/TUI subcommand (cheap) or pick Avalonia for that target (later).

## Recommended build order

1. **Audit `ClaudeCycler.Core` for platform leaks.** Run `grep -ri "user32\|DllImport\|win32\|HWND" ClaudeCycler.Core/`. Anything that surfaces is a portability point. Map each to its abstraction layer.
2. **Carve out `IClaudeProcessLocator` and `IClaudeWindowFocuser`.** Windows impl is the existing code; mac impl uses `CGWindowListCopyWindowInfo` + `NSRunningApplication`; Linux impl uses `xdotool`/`wmctrl` (X11) or skip GUI for v1.
3. **Build the dashboard view in MAUI.** Card per session: cwd, session-id (truncated), last-event timestamp, badge from `SessionLivenessVerifier`, click-to-focus button. Sort waiting-for-input to the top (matches the Muxara UX the user liked).
4. **Hook into transcript change notifications.** `FileSystemWatcher` works cross-platform on `~/.claude/projects/`. Debounce; re-classify only the touched file.
5. **(Stretch) Cross-machine.** User has `~/.claude/notes/reference_memory_sync.md` and a sync script for memory; a similar approach could surface llamabox sessions over the existing Y: share. Defer past v1.

## Non-obvious context the next session should know

- **Subagent transcripts are separate JSONL files** under `~/.claude/projects/<slug>/<sessionId>/subagents/agent-<agentId>.jsonl`. The dashboard probably wants to roll these up under the parent session (don't list each subagent as its own card by default), but their state is independently meaningful — a parent session can be `Resolved` while a subagent is still working. See `~/.claude/CLAUDE.md` "Claude Code transcripts & cost" section.
- **Session 0 quirk** on chonkers — see `~/.claude/notes/feedback_s4u_session0_kill.md`. May affect process enumeration.
- **Windows hWND quirks** — see `~/.claude/notes/reference_windows_conpty_hwnd.md` and `reference_windows_claude_cwd_handles.md` if the existing locator code starts misbehaving during the audit.
- **CLAUDE.md style rules** are strict here (var everywhere, file-scoped namespaces, no `this.`, no default modifiers). Re-read `CLAUDE.md` before writing C#.
- **Build with MSBuild, not `dotnet build`** — native C++ dependency. Already documented in `CLAUDE.md`.

## Prior art surveyed (2026-05-09)

These are the closest existing tools — none fit, hence building this in MegaSchoen:

- **Muxara** (https://dev.to/mani_kolbe_510e0016d2a32e/i-built-a-free-dashboard-for-managing-parallel-claude-code-sessions-macos-open-source-2ai8) — closest in UX (live status cards, auto-detected state, click-to-switch). macOS-only, tmux-oriented. MIT, Tauri (Rust + React). Worth studying for UX choices; the "needs-input sorts to top" behavior is the right default.
- **claude-dashboard** (https://github.com/Tpain166/claude-dashboard) — TUI, cross-platform, real-time monitoring. Read the JSONL-tailing code if `SessionLivenessVerifier` needs extension.
- **cmux** (https://github.com/manaflow-ai/cmux) — Ghostty-based macOS app, vertical tabs with branch / PR / port info. Inspiration for what to put on each card beyond just state.
- **Building a Real-Time Dashboard for Claude Code Session Management** (https://www.ksred.com/managing-multiple-claude-code-sessions-building-a-real-time-dashboard/) — design write-up of a similar dashboard; useful for cross-checking architecture choices.

## Open questions for the next session

- Does the dashboard live as a **new MAUI page in MegaSchoen** (sibling to Display Manager) or a **separate executable** that reuses `ClaudeCycler.Core`? Probably the former for cohesion, but worth confirming with the user.
- What's the click-to-focus story when the target session is on a remote machine (llamabox)? SSH + tmux attach? Defer.
- Should the dashboard surface **cost / token usage** per session? `~/.claude/statusline-command.sh` lines 36–95 already implement the canonical pricing formula; could be reused. Probably v2.

## Definitely out of scope for v1

- Spawning new sessions from the dashboard (Muxara does this; not requested here).
- Cross-machine view — defer past v1.
- Mobile platforms — explicitly not a target.
- Replacing claude-history's retrospective view — this dashboard is specifically about **active** sessions; claude-history covers the history side.
