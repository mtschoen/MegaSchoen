# Layout Editor — Screenshot-Iteration Loop + UI Polish (next session)

> Handoff from the 2026-05-29 session. **gitea PR #13** (base `main`, mergeable) carries Phase 1 + Phase 2 +
> feedback items 1–7. The editor works on the rig (snap, identify, add/remove/reset, primary/rotate redraw all
> confirmed). This plan sets up a **scripted screenshot loop** so the next session can iterate on the editor's
> layout/visuals from screenshots instead of slow verbal round-trips, and lists the remaining UI-polish items to
> fix that way.

## Why this plan

Pixel-level layout work over verbal feedback is too slow. git-wizard already proved a screenshot loop is worth it
— BUT its mechanism is **Avalonia headless** (`AppBuilder.Configure<App>().UseHeadless(...)` +
`window.CaptureRenderedFrame()` in `git-wizard/GitWizardUI.Screenshot/Program.cs`, run via
`.gitea/workflows/screenshot.yml`). **That API does not exist for .NET MAUI / WinUI 3.** MAUI on Windows renders
through WinUI 3, which needs a real HWND + running message loop + display connection — there is no headless
backend. So MegaSchoen needs a different capture path. Do **not** try to port git-wizard's tool.

## Outstanding UI-polish items (the things to iterate on via screenshots)

1. **Window still slightly too small.** Prefer **shrinking control sizes + padding** over growing the window again
   (it's at 1100×820 now — `DisplayManagerPageViewModel.OpenLayoutEditor`). Tighten button heights, `Spacing`, and
   `Padding` in `LayoutEditorPage.xaml`'s side panel so the whole panel fits without scrolling.
2. **Controls jump around on select/deselect.** The side-panel selection block
   (`IsVisible="{Binding HasSelection}"`) collapses when nothing is selected, reflowing everything below it. The
   user finds the movement disorienting. **Fix (user's lean):** keep the selection controls always laid out and
   **disable/ghost** them when `HasSelection` is false, instead of collapsing — so nothing reflows. Alternative the
   user offered: move the persistent Test/Stash/Commit action bar to the **top** so it never moves. Goal = no
   reflow on selection change.

## Recommended screenshot mechanism: in-app `--screenshot-editor` debug mode

Build the capture **into the app** (in-process, deterministic — avoids UI automation to click the ✎ Edit button):

1. **Launch arg** — parse `--screenshot-editor <out.png>` at startup (`MauiProgram`/`App.xaml.cs`, `#if WINDOWS`).
2. **Seed a synthetic preset** — do NOT depend on the user's real `configs.json` profiles or live hardware. Build a
   fixed `SavedDisplayProfile` with ~3–4 `SavedDisplayConfig`s that exercise every UI state: a primary, a 90° and a
   180° rotated monitor, varied sizes, a negative-coordinate monitor. That makes the screenshot show the ★ PRIMARY
   caption, the ▲▶▼◀ orientation arrows, the Add/Remove/Reset panel, etc. **Never persist this seed to disk.**
3. **Open the editor window directly** for that preset on startup (reuse `LayoutEditorViewModel` +
   `LayoutEditorPage` + `Application.Current.OpenWindow`), bypassing the main shell.
4. **Self-capture** — after the window is Activated and laid out (hook `Loaded`/`SizeChanged` to settle, or a short
   `DispatcherQueue` delay), capture its own HWND, write the PNG, then `Application.Current.Quit()`.

### Capture API (WinUI 3, real HWND)

Use **`PrintWindow` with `PW_RENDERFULLCONTENT` (0x00000002)** — slow but reliable, and it captures a composited
window **without** needing foreground/visibility (avoids flaky bring-to-front). Get the HWND the same way the
identify overlay already does: `WinRT.Interop.WindowNative.GetWindowHandle(window)` (see
`LayoutEditorPage.ShowIdentifyOverlay`).

```csharp
[DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
const uint PW_RENDERFULLCONTENT = 0x00000002;
// GetWindowRect(hwnd) → CreateCompatibleDC + CreateCompatibleBitmap at physical px
// → PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT) → Bitmap → PNG
// (System.Drawing.Common, or SkiaSharp which the app already references).
```

**Alternatives considered (and why not first):** `Graphics.CopyFromScreen`/`BitBlt` needs the window foreground +
visible and is flaky for accelerated content; `Windows.Graphics.Capture` is modern and captures occluded windows
but needs Direct3D setup + `InitializeWithWindow` — heavier than warranted. Start with PrintWindow; fall back to
`Windows.Graphics.Capture` only if PrintWindow returns black (see risks).

### Thin wrapper script

`.claude/scripts/shoot-editor.ps1`: `Stop-Process -Name MegaSchoen -Force -EA SilentlyContinue`, build the
Windows-debug app, run `MegaSchoen.exe --screenshot-editor <repo>\Screenshots\layout-editor.png`, return the PNG
path so the agent can Read it. Output dir `Screenshots/` (mirrors git-wizard's convention). Per the user's
`.claude/scripts/` rule, delete it after the loop unless kept intentionally — but this one is reusable, so consider
promoting it to a repo-level script.

## Open questions / risks

- **Black capture:** `PrintWindow` on a WinUI 3 (DWM/Composition) window can return black for some
  hardware-composited content. If the first PNG is black, fall back to `Windows.Graphics.Capture` or a brief
  foreground + `CopyFromScreen`. Verify on the very first run before building the whole loop on it.
- **DPI:** capture at the window's physical pixel size (`GetWindowRect` is physical px); the rig runs at non-100%
  scaling — confirm the PNG isn't half-size / cropped.
- **Capture scope:** full window (incl. title bar) is fine for layout iteration; client-area-only is optional.
- **Settle timing:** the canvas fit-to-view runs on `SizeChanged`; make sure capture happens *after* the first
  layout pass or the monitors won't be positioned yet.

## Carried over from the retired feedback plan (`2026-05-29-layout-editor-feedback.md`, now `git rm`'d — items 1–7 done & PR'd)

- **Item 8 (STILL OPEN):** all-TFM **Release** build is broken — `SessionsPage.xaml:27`
  `x:DataType="vm:HostStatusViewModel"` references a `#if WINDOWS`-only VM, so Release XamlC fails to resolve it on
  the android/ios/maccatalyst TFMs (Debug skips XamlC). From the remote-sessions feature; would fail on `main` too.
  Options: guard/by-platform the `DataTemplate`, or trim the unused mobile TFMs (app is Windows-only in practice).
  **User hasn't decided — ask before touching the remote-sessions feature.** Windows-only Debug + the Windows-only
  Release command below both work fine.
- **On-rig follow-ups (deferred):** `LayoutEditorViewModel.TestAsync` applies synchronously on the UI thread (a
  multi-second GPU switch blocks the editor window — consider offloading); canvas drag uses a DIP-agnostic scale
  (may need a DPI density factor at non-100% scaling).
- **Not yet exercised:** Test/Stash/Commit against real profiles (user is dogfooding); native full-field apply
  round-trip (rotation/resolution/refresh) on the multi-GPU rig.

## Build / run (inherited)

- MSBuild full path (not on PATH): `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`.
- Debug app (via the SOLUTION — the `.sln` maps x64): `MSBuild MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false`.
  Kill `MegaSchoen.exe` first (`Stop-Process -Name MegaSchoen -Force -EA SilentlyContinue`) — SingleInstance silently
  swallows relaunches.
- Windows-only Release (the user's shortcut; all-TFM Release is item 8): `MSBuild MegaSchoen.sln -t:Restore` then
  `MSBuild MegaSchoen\MegaSchoen.csproj -p:Configuration=Release -p:Platform=x64 -p:TargetFramework=net10.0-windows10.0.26100.0 -nodeReuse:false`.
- App exe: `MegaSchoen\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MegaSchoen.exe`.
- Tests: build `DisplayManager.Core.Tests` then `dotnet vstest DisplayManager.Core.Tests\bin\Debug\net10.0\DisplayManager.Core.Tests.dll`
  (MSTest 4.x — `[TestMethod]`/`Assert.AreEqual`/`Assert.ThrowsAsync`; **39 green**, incl. 7 `LayoutSnapperTests`).

## Key files

- `MegaSchoen/LayoutEditorPage.xaml(.cs)` — side-panel layout (the jump + sizing live here); `ShowIdentifyOverlay`
  (existing `GetWindowHandle` pattern to copy for capture); `BuildRectContent` (primary caption + orientation arrow).
- `MegaSchoen/ViewModels/LayoutEditorViewModel.cs` — `HasSelection` (drives the collapsing block), command enables.
- `MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs` — `OpenLayoutEditor` (window size 1100×820; screenshot mode
  would call a similar `OpenWindow`).
- `MegaSchoen/MauiProgram.cs` + `MegaSchoen/App.xaml.cs` — startup; where to parse `--screenshot-editor`.
- git-wizard (reference only — Avalonia-headless, do NOT copy): `git-wizard/GitWizardUI.Screenshot/Program.cs`,
  `git-wizard/.gitea/workflows/screenshot.yml`.
