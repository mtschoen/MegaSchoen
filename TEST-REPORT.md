MegaSchoen test report — 2026-07-28T10:38:08-07:00
=================================================

Status:   PASS
Tests:    3 MegaSchoen.Avalonia.Tests, all passing
Git:      8c4ae71 (agent/1186-avalonia-tray-close-cancel-vetoe)
Coverage: 3/3 statements (100%)
          0 lines uncovered
          0 exclusion annotations

Scope:
  - The new Avalonia shutdown coordinator is measured with coverlet.msbuild.
  - Both close-policy states are covered: normal window close hides to the
    tray, while a platform shutdown request allows the window to close.
  - The shutdown flag's volatile CLR modifier is verified so a platform
    shutdown write is visible to a close-path read on another thread.
  - CI merges this Cobertura report with the existing Claude.Core report.

Existing baseline:
  - Claude.Core: 828/900 statements (92.0%), last measured 2026-06-27.
  - The pr-crew/coverage gate threshold is 80%.
  - Claude.Core was not remeasured because this change does not modify it.

Verification:
  - `dotnet test MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release /p:CollectCoverage=true`: 3 passed; 100% line, branch, and
    method coverage for `ShutdownCoordinator`.
  - `dotnet build MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release --no-restore -warnaserror`: 0 warnings, 0 errors.
  - `roslynator analyze MegaSchoen.Avalonia.Tests/... --severity-level
    warning`: 0 diagnostics.
  - Built `MegaSchoen.Avalonia --hidden` stayed running for the five-second
    Xvfb smoke window with fake processes and isolated writable state.
  - Solution whitespace verification passes with the CRLF checkout used by
    Windows CI. The Linux worktree stores LF, so the unadjusted local command
    reports repository-wide line-ending drift without source-format findings.

Known repository-wide gate state:
  - Local `aislop ci .` 0.14.0 reports the documented 183 Linux-checkout C#
    line-ending warnings plus one pre-existing C++ lint finding in
    `DisplayManagerNative.ApplyConfiguration.cpp`; neither originates in this
    scoped Avalonia change.
