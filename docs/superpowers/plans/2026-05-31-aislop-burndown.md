# Handoff: aislop burndown to 100/100

> **STATUS: DONE (2026-05-31)** - all 34 findings cleared, `aislop scan .` = 100/100,
> `aislop ci .` exit 0, clean `-t:Rebuild` = 0 analyzer warnings, 155 logic tests green.
> The CI aislop job stays gated as planned (the C#-capable fork is still not on npm).
> See the updated aislop section in `TEST-REPORT.md`.

**Created:** 2026-05-31
**Base:** `main` @ `8e89d01` (linter rollout fully landed; Roslyn/Roslynator/cppcheck/format all 0, CI green)
**Goal:** drive the gated aislop AI-slop gate from **22/100 (34 findings)** to **100/100**, then un-gate the CI aislop job.

## Context

The linter rollout (PR #14 + fix `8e89d01`) enforces Roslyn + Roslynator + MSTest analyzers,
clang-format/cppcheck, and `dotnet format whitespace` - all at zero, all CI-enforced. aislop is the
remaining gate. Its C# engine works only via the local `mtschoen/aislop` fork (v0.10.1, not on npm),
so its CI job in `.gitea/workflows/ci.yml` stays commented-out until the score is clean AND the engine
is publishable/vendorable.

Run the scan with: `aislop scan .` (or `aislop scan . --format json` for machine-readable).
Config: `.aislop/config.yml` (failBelow=80). Re-scan after each batch to track the score.

## The 34 findings, by rule (exact locations as of 8e89d01)

### 1. csharp-console-leftover (13) - Debug/Trace diagnostic output
All are `System.Diagnostics.Debug.WriteLine(...)` left in shipped code. NOT bugs - they are
genuine debug tracing. Decide ONE policy and apply uniformly:
- **Option A (recommended):** route through the existing `Claude.Core.Logger` (or a new
  `[Conditional("DEBUG")]` trace helper) so it is intentional, not leftover. Logger already exists
  and is the project's logging idiom.
- **Option B:** delete the ones that were genuinely temporary.
- Do NOT blanket-suppress the rule - that defeats the gate.

Locations:
- `DisplayManager.Core/DisplayManager.cs:53, :102`
- `DisplayManager.Core/Services/LayoutDraftStore.cs:62`
- `DisplayManager.Core/Services/ProfileStorageService.cs:53, :84`
- `MegaSchoen/Platforms/Windows/Services/GlobalHotkeyService.cs:68, :84, :89, :100, :116, :120, :143, :150`

### 2. csharp-null-forgiving (11) - the `!` null-forgiving operator
Each silences a nullable warning without proving non-null. Fix per-site by proving it (pattern match,
`is not null` guard, `?? throw`, or restructuring) rather than asserting. Mostly MAUI/interop glue.

Locations:
- `Claude.Core/ActiveSessionEnumerator.cs:42, :138, :144, :146`
- `Claude.Core/HookDispatcher.cs:89, :98`
- `Claude.Core/SettingsJsonInstaller.cs:51, :69, :77`
- `MegaSchoen/Platforms/Windows/Services/StartupService.cs:49` (the `Activator.CreateInstance(...)!` COM shell)
- `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs:161`

### 3. swallowed-exception (4) - AUDITED as intentional best-effort
These are NOT bugs. Each is a deliberate "never throw from here" pattern:
- `Claude.Core/Logger.cs:18` - logging must never throw (runs inside hook handlers)
- `Claude.Core/HookCapture.cs:43` - the capture tee must never throw
- `Claude.Core/Remote/SshStreamProcess.cs:42` - remote stream resilience
- `MegaSchoen/App.xaml.cs:57` - startup guard
**Recommended fix:** add a per-site `// aislop-disable-line ai-slop/swallowed-exception` (or the
fork's inline-suppress syntax - CHECK what `mtschoen/aislop` supports) WITH the existing explanatory
comment, OR restructure to `catch (Exception ex) { Logger.Log(...); }` where a log is acceptable
(Logger.cs itself obviously can't log through itself). Document the justification inline either way.
Do NOT rethrow - that breaks the resilience these guards exist for. This is the escalate-over-shortcut
boundary: per-case justified suppression is legitimate here; blanket category suppression is not.

### 4. file-too-large (3, max 400 lines)
- `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs` (495) - split by concern (key mapping vs
  window ops vs hotkey registration), or extract the key-name->VK table.
- `MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs` (522) - extract hotkey-capture state machine
  and/or the profile-CRUD commands into a partial or collaborator.
- `MegaSchoen/ViewModels/LayoutEditorViewModel.cs` (672) - the biggest; split test->stash->commit
  gate logic from the canvas/drag state. Per AGENTS.md prefer a `foo/` package-style split along
  natural domain boundaries, not arbitrary line-count chopping.

### 5. function-too-long (2, max 80 lines)
- `Claude.Core/ActiveSessionEnumerator.cs:27` `DefaultProjectsRoot` (131 lines) - extract helpers.
- `MegaSchoen/Platforms/Windows/App.xaml.cs:44` `InitializeWindowsServices` (201 lines) - this is the
  big DI + hotkey + tray wireup; break into `RegisterServices`, `RegisterHotkeys`, `InitTray`, etc.
  (note: this method was made `static` in 8e89d01, so extracted helpers must be static too.)

### 6. csharp-redundant-doc-comment (1)
- `DisplayManager.Core/Models/DriftReport.cs:12` - XML-doc summary restates the member name. Either
  add real information or remove the redundant `<summary>`.

## Suggested order (fastest score gain first)

1. **redundant-doc-comment (1)** + **swallowed-exception (4)** - quick, high-confidence. (~5 findings)
2. **console-leftover (13)** - one uniform policy decision, then mechanical. (~13 findings)
3. **null-forgiving (11)** - per-site, needs care but each is small. (~11 findings)
4. **file/function size (5)** - the real refactors; do last, one file at a time, re-run tests after each.

After each batch: `aislop scan .` to confirm the score climbing; full `MSBuild MegaSchoen.sln` stays at
0 warnings (analyzers are now enforced - don't regress them); `dotnet test` both test projects green
(156 tests). Build with VS18 MSBuild per AGENTS.md.

## Definition of done

- `aislop scan .` reports **100/100, 0 findings**.
- Full solution still 0 analyzer warnings; 156 tests green; cppcheck + whitespace still clean.
- Un-gate the aislop CI job in `.gitea/workflows/ci.yml` (uncomment it) ONLY once the engine is
  installable in CI - either `mtschoen/aislop` is published to npm with C# support, or the fork is
  vendored / installed-by-path in the workflow the same way cppcheck's MSI is. Until then, leave the
  job gated even at 100/100 and note the score in `TEST-REPORT.md`.
- Update `TEST-REPORT.md` aislop section to reflect the new score.

## Watch-outs (from the rollout session)

- `dotnet format analyzers` CORRUPTS raw-string literals (`"""..."""` + concat) - do size refactors by
  hand, not via the auto-formatter, near any `"""` raw strings (several test files have them).
- An incremental build reports a deceptive "0 warnings" - always `-t:Rebuild` to truly validate.
- Don't blanket-suppress any aislop rule category - the gate's value is the per-case forcing function.
  Per-site justified suppression (with a comment) is the escalate-over-shortcut-approved path for the
  4 intentional swallows only.
