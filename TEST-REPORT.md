MegaSchoen test report ΓÇö 2026-07-28T11:45:00-07:00
=================================================

Status:   PASS
Tests:    228 Claude.Core.Tests + 3 MegaSchoen.Avalonia.Tests, all passing
Git:      7ca728a (main, after the 2026-07-28 merge wave: #43 #44 #45 #46 #47)
Coverage: Claude.Core 1060/1139 lines (93.06%), branch 86.5%, method 95.78%
          MegaSchoen.Avalonia ShutdownCoordinator 3/3 statements (100%)

Scope:
  - Repo-wide re-measure on the Windows TFM after the merge wave landed the
    tray/logout fix (#44), /wrap status (#46), session mode badges (#47),
    session cwd display (#45), and the lint-cpp policy change (#43).
  - Measured with `pwsh .claude/scripts/measure-coverage.ps1` (the documented
    scope CI uses); the Avalonia shutdown coordinator is measured separately
    with coverlet.msbuild and merged by CI into the posted status.
  - The pr-crew/coverage gate threshold is 80%; the posted main status is
    green.

Lowest-covered files (from the same measurement):
  - Logger.cs 78.6%, SessionStateClassifier.cs 83.3%,
    Remote/RemoteSessionStreamClient.cs 83.9%, EnvironmentProcessLocator.cs
    85.0%, StateStore.cs 85.6%.

Notes:
  - Prior report was PR #44's scoped pre-merge report; branch-side
    TEST-REPORT.md copies were intentionally dropped during the wave's
    conflict resolution, so this file is the first post-wave baseline.
