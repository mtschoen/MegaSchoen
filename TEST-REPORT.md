MegaSchoen test report — 2026-07-28T11:45:00-07:00
=================================================

Status:   PASS
Tests:    228 Claude.Core.Tests + 3 MegaSchoen.Avalonia.Tests
          + 45 DisplayManager.Core.Tests, all passing
Git:      7ca728a (main baseline after #43 #44 #45 #46 #47)
          41275bf (DisplayApplyGate branch measurement)
Coverage: Claude.Core 1060/1139 lines (93.06%), branch 86.5%, method 95.78%
          MegaSchoen.Avalonia ShutdownCoordinator 3/3 statements (100%)
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
    new serialization and cooldown policy at the managed/native apply boundary.
    Tests cover accepted applies, concurrent drops, cooldown drops, cooldown
    expiry, diagnostic logging, action suppression, and exception cleanup.
    DisplayManager.Core remains outside the automated pr-crew coverage scope
    because it is coupled to the Windows-native display project.

Lowest-covered files (from the same measurement):
  - Logger.cs 78.6%, SessionStateClassifier.cs 83.3%,
    Remote/RemoteSessionStreamClient.cs 83.9%, EnvironmentProcessLocator.cs
    85.0%, StateStore.cs 85.6%.

Verification:
  - `dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj
    -c Release`: 45 passed.
  - The DisplayManager managed projects build with warnings as errors after
    omitting the native vcxproj reference through a temporary Linux-only
    MSBuild test override.
  - The normal Linux build reaches the expected `MSB4278` because the Windows
    C++ targets are unavailable; the authoritative full solution build remains
    the Windows VS 18 MSBuild CI gate.
  - Local `aislop scan .` 0.14.0 found 0 code-quality, AI-slop, security, or
    lint issues. Its only findings were the repository-documented whole-file
    CRLF warnings on the LF Linux checkout.

Notes:
  - The repository-wide figures are the first post-wave baseline; the
    DisplayApplyGate figures preserve this branch's scoped measurement.
