MegaSchoen test report — 2026-07-28T20:40:37-07:00
=================================================

Status:   PASS (available Linux merge verification)
Tests:    61 DisplayManager.Core.Tests + 42 MegaSchoen.Avalonia.Tests,
          all passing
Git:      39e0b65 (PR #62 head merged with origin/main 733c3cb)
Coverage: MegaSchoen.Avalonia merged scoped code 424/465 lines (91.18%),
          270/298 branches (90.60%), and 90.69% methods
          41 lines uncovered, 0 exclusion annotations

Scope:
  - Issue #59 adds the hostable Avalonia visual layout editor, including
    desktop-to-canvas geometry, pointer drag, optional edge snapping,
    normalization, and the Test → Stash → Commit gate.
  - Issue #60 adds Avalonia's Windows profile hotkeys, HKCU login autostart,
    single-instance activation, and x64/native packaging contract.
  - The merged Avalonia coverage scope includes ShutdownCoordinator, the
    Layout* view-models, and issue #60's platform-neutral integration types.
    The measured 91.18% line coverage exceeds the pr-crew 80% threshold.
  - DisplayManager.Core domain semantics were reused unchanged. Its native
    C++ project build edge now requires Windows full MSBuild, so Core MSBuild
    can compile the managed services without attempting an unsupported
    `.vcxproj` build.

Prior recorded scopes (not rerun by this conflict resolution):
  - Claude.Core: 944/1067 lines (88.47%) on net10.0; the last recorded
    Windows baseline was 93.06%.
  - DisplayManager.Core merged scope: 1036/1242 lines (83.41%) and 230/310
    branches.

Verification:
  - `dotnet test MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release -warnaserror /p:CollectCoverage=true`: 42 passed; 91.18% line,
    90.60% branch, and 90.69% method coverage in the merged configured scope.
  - `dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj
    -c Release -warnaserror`: 61 passed.
  - `LayoutEditorPageLoadsCompiledAxaml` instantiated the compiled Avalonia
    UserControl in a bounded test process.
  - Main's issue #60 report records the win-x64 Release build, x64 apphost,
    startup-command smoke, and authoritative Windows CI package smoke.

Platform limits:
  - This Linux host cannot run Visual Studio 18 MSBuild or launch the Windows
    Release app. The `package-windows` CI job is the authoritative
    Release/native runtime validation.
  - `.editorconfig` mandates CRLF while git stores LF on this checkout.
    Windows checkout/CI remains authoritative for whole-tree formatting.
  - This issue intentionally supplies a hostable editor surface; wiring it
    into the Avalonia Display Manager page belongs to the separate page-shell
    slice.
