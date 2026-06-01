MegaSchoen test report - 2026-05-31

Status:   PASS
Mode:     close-the-gap (linter rollout) / best-effort (aislop)
Tests:    156 total (117 Claude.Core.Tests + 39 DisplayManager.Core.Tests)
Git:      fix/linter-analyzers-not-enabled, off main ea35948 (post-#14)
Coverage: not instrumented (no line-coverage tool wired yet; tracked follow-up)

Lint (enforced gates - all GREEN, analyzers now ACTUALLY enabled):
  dotnet format whitespace : 0 changes (--verify-no-changes clean, whole solution)
  Roslyn analyzers         : 0 findings (EnableNETAnalyzers, AnalysisLevel=latest-Recommended)
  Roslynator.Analyzers     : 0 findings (4.15.0, C# projects)
  MSTest analyzers         : 0 findings (MSTEST0001/0037/0052 etc.)
  cppcheck (--enable=all)  : 0 findings (DisplayManagerNative.cpp; json.hpp excluded)
  Build                    : full MSBuild solution rebuild -> 0 warnings, 0 errors (8 projects);
                             3 CI projects rebuild clean with -warnaserror

  Gate mechanism: dotnet build -warnaserror in CI for the dotnet-buildable managed
  projects (Claude.Core.Tests, ClaudeSessionsCLI, ClaudeHookBridge); full-solution
  MSBuild -warnaserror locally for the native-referencing / MAUI projects.

  IMPORTANT (why this report changed): PR #14 merged with the analyzer ENABLEMENT
  missing - the Directory.Build.props Roslynator block and the .editorconfig CA-rule
  config were lost in a branch reset, so EnableNETAnalyzers/Roslynator never ran and
  the green "-warnaserror" gate was hollow (no analyzers => no warnings to fail on).
  This change restores both config pieces and drives the ~104 findings they surface
  to zero across the whole solution. The CA1707 test-method exemption was widened to
  include DisplayManager.Core.Tests (added by #13, absent from the original config).
  Always rebuild (-t:Rebuild) to validate the analyzer gate - an incremental no-op
  build reports a deceptive "0 warnings".

  Representative fixes (recipe mirrors the original 589ee51 cleanup):
    - Interop User32/Kernel32 made internal (CA1401 P/Invoke visibility + CA1707 constants)
    - culture-aware string ops: ToLowerInvariant, StartsWith(char)/StringComparison.Ordinal,
      int.ToString(CultureInfo.InvariantCulture) (CA1304/1305/1310/1311/1866)
    - cached JsonSerializerOptions (CA1869); concrete return/param types (CA1859)
    - StartupService -> static class; private/instance helpers -> static (CA1822); sealed
      converters (CA1852); redundant initializers removed (CA1805)
    - IntPtr->int explicit checked (CA2020); P/Invoke marshaling BestFitMapping=false (CA2101);
      screenshot-test P/Invoke -> char[] + discards (CA1838/CA1806)
    - ProfileCollection -> ProfileConfiguration (CA1711, config root not an ICollection)
    - SessionsPageViewModel: drop unused INotifyPropertyChanged/PropertyChanged (CS0067)
    - [assembly: DoNotParallelize] for DisplayManager.Core.Tests + MegaSchoen.UITests (MSTEST0001)
    - obsolete DisplayAlert -> DisplayAlertAsync (CS0618)

aislop (AI-slop gate - READY-BUT-GATED, NOT enforced in CI):
  Engine: local mtschoen/aislop fork, v0.10.1 (C#-capable). Score 22/100 ("Critical"),
  ~34 findings (unchanged by this analyzer pass). CI aislop job stays gated: the
  C#-capable engine is not on npm (upstream npm aislop has no C# support).

  Baseline (recorded; deferred to a tracked follow-up):
     4  swallowed-exception (err) - AUDITED: intentional best-effort resilience patterns
        (process enumeration, optional/locked state-file IO, window-focus/hotkey). NOT
        rethrown; no bug-hiding swallow.
    13  csharp-console-leftover  - Debug/Trace diagnostic output in MAUI/Windows glue.
    11  csharp-null-forgiving    - mostly MAUI binding idiom.
     5  file-too-large / function-too-long - structural; splitting deferred.
     1  redundant-doc-comment.

Follow-up (tracked): per-case justify-or-restructure the swallowed-exception sites,
gate or remove the Debug/Trace leftovers, null-safe the converters, split the oversized
files/functions, then un-gate the CI aislop job once a C#-capable aislop is on npm.
