MegaSchoen test report — 2026-07-28T23:21:23-07:00
=================================================

Status:   PASS (available Linux merge verification)
Tests:    63 DisplayManager.Core.Tests + 52 MegaSchoen.Avalonia.Tests,
          all passing
Git:      b21b0a2 merged with origin/main 938661f (merge in progress)
Coverage: MegaSchoen.Avalonia merged scoped code 648/733 lines (88.40%),
          298/344 branches (86.62%), and 86.36% methods
          85 lines uncovered
          0 exclusion annotations added

Scope:
  - Issue #58 ports the Display Manager profile workflow to Avalonia:
    display/profile cards, save/apply/delete, inline confirmations, hotkey
    assignment and clearing, and overwrite-from-current.
  - Issue #59 adds the hostable Avalonia visual layout editor, including
    desktop-to-canvas geometry, pointer drag, optional edge snapping,
    normalization, and the Test → Stash → Commit gate.
  - Issue #60 adds Avalonia's Windows profile hotkeys, HKCU login autostart,
    single-instance activation, and x64/native packaging contract.
  - The merged Avalonia coverage scope combines ShutdownCoordinator, the
    DisplayManager* and Layout* view-models, and the platform-neutral
    lifecycle, gesture, display-profile dispatch, startup-command, and
    packaging verification code. The measured 88.40% line coverage exceeds
    the pr-crew 80% threshold.
  - CI measures Claude.Core separately on the Windows TFM and merges that
    report with the Avalonia report.
  - DisplayManagerNative remains in full Visual Studio MSBuild graphs while
    being excluded from Core MSBuild graphs used by `dotnet test`.

Prior recorded scopes (not rerun by this conflict resolution):
  - Claude.Core: 944/1067 lines (88.47%) on net10.0; the last recorded
    Windows baseline was 93.06%.
  - DisplayManager.Core merged scope: 1036/1242 lines (83.41%) and 230/310
    branches.

Verification:
  - `dotnet test MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release -warnaserror /p:CollectCoverage=true`: 52 passed; 88.40% line,
    86.62% branch, and 86.36% method coverage in the merged configured scope.
  - `dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj
    -c Release -v minimal -warnaserror`: 63 passed.
  - Main's issue #60 report records the win-x64 Release build, x64 apphost,
    startup-command smoke, and authoritative Windows CI package smoke.

Platform limits:
  - This Linux host cannot run Visual Studio 18 MSBuild or launch the Windows
    Release app. The `package-windows` CI job remains the authoritative
    Release/native runtime validation.
  - `.editorconfig` mandates CRLF while git stores LF on this checkout.
    Windows checkout/CI remains authoritative for whole-tree formatting.
  - This merge intentionally preserves the hostable layout editor surface;
    wiring it into the Avalonia Display Manager page belongs to the separate
    page-shell slice.
