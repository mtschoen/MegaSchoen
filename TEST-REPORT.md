MegaSchoen test report — 2026-07-28T12:38:15-07:00
=================================================

Status:   PASS
Tests:    228 Claude.Core.Tests + 3 MegaSchoen.Avalonia.Tests
          + 61 DisplayManager.Core.Tests, all passing
Git:      7ca728a (main baseline after #43 #44 #45 #46 #47)
          41275bf (DisplayApplyGate branch measurement)
          872668c (PR #50 merge head)
Coverage: Claude.Core 1060/1139 lines (93.06%), branch 86.5%, method 95.78%
          MegaSchoen.Avalonia ShutdownCoordinator 3/3 statements (100%)
          DisplayManager.Core merged scope 1036/1242 lines (83.41%),
          230/310 branches
          DisplayManager.Core DisplayApplyGate 30/30 lines (100%),
          6/6 branches (100%), 3/3 methods (100%)

Scope:
  - Repo-wide re-measure on the Windows TFM after the merge wave landed the
    tray/logout fix (#44), /wrap status (#46), session mode badges (#47),
    session cwd display (#45), and the lint-cpp policy change (#43).
  - Measured with `pwsh .claude/scripts/measure-coverage.ps1` (the documented
    scope CI uses); the Avalonia shutdown coordinator is measured separately
    with coverlet.msbuild and merged by CI into the posted status.
  - The pr-crew/coverage gate threshold is 80%; the posted main status is
    green.
  - Coverlet separately measured `DisplayManager.Core.DisplayApplyGate`, the
    serialization and cooldown policy at the managed/native apply boundary.
    Tests cover accepted applies, concurrent drops, cooldown drops, cooldown
    expiry, diagnostic logging, action suppression, and exception cleanup.
    DisplayManager.Core remains outside the automated pr-crew coverage scope
    because it is coupled to the Windows-native display project.

PR #50 changed scope:
  - `DisplayApplyCooldown`: 100% line and branch coverage.
  - The deferred `ApplyConfiguration` return path is covered, including
    structured retry metadata and diagnostic logging.
  - Cooldown behavior is covered before resume, during the interval, at expiry,
    when a second resume restarts the interval, and across independent core
    instances through the shared monotonic resume timestamp.
  - Missing, malformed, future, and unreadable shared timestamps are covered,
    along with wall-clock correction, named-mutex contention, abandoned-mutex
    recovery, and persistence failure retaining the in-process guard.

Lowest-covered files (from the same main measurement):
  - Logger.cs 78.6%, SessionStateClassifier.cs 83.3%,
    Remote/RemoteSessionStreamClient.cs 83.9%, EnvironmentProcessLocator.cs
    85.0%, StateStore.cs 85.6%.

Verification:
  - `dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj
    -f net10.0 -c Release` with the documented Linux managed-only targets
    override: 61 passed after conflict resolution.
  - The merged suite under Microsoft Code Coverage produced Cobertura results:
    1036/1242 lines and 230/310 branches overall.
  - `dotnet build DisplayManager.Core/DisplayManager.Core.csproj -f net10.0
    -c Release -warnaserror` with that override: 0 warnings, 0 errors.
  - `roslynator analyze` on DisplayManager.Core and
    DisplayManager.Core.Tests: 0 diagnostics.
  - `aislop scan .`: 0 code-quality, AI-slop, or security findings.

Platform limits:
  - The Linux worktree cannot build or launch the Windows MAUI host because the
    MAUI/Visual Studio native workload is unavailable. The Windows CI solution
    build remains the authoritative check for `SystemEvents` host wiring.
  - The Linux checkout stores LF while `.editorconfig` mandates CRLF, so the
    local formatter and aislop report the documented whole-tree line-ending
    warnings. Windows CI checks out CRLF and runs the authoritative format gate.
