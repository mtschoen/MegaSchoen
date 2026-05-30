MegaSchoen test report — 2026-05-29
═══════════════════════════════════════════

Status:   PASS
Mode:     close-the-gap (this work establishes the lint gate)
Tests:    96 total — Claude.Core.Tests (95 on net10.0, 96 on net10.0-windows), 0 failed
Git:      chore/linter-rollout (lint rollout: config + format sweep + hand-fixes + gate)

Coverage: NOT instrumented in this repo.
          No coverage collector is configured (this PR scopes the LINT gate, not
          coverage). Coverage tracking is a separate follow-up — recorded here as the
          honest baseline, not claimed as 100%.

Lint (C#): 0 findings — the bar is met.
          Authoritative gate: `MSBuild MegaSchoen.sln -p:Configuration=Debug` builds the
          WHOLE solution (native C++ + MAUI all TFMs + every managed project) with
          0 analyzer warnings.
            Roslyn analyzers (EnableNETAnalyzers, AnalysisLevel=latest-Recommended): 0
            Roslynator.Analyzers 4.15.0:                                            0
            MSTest analyzers:                                                       0
            CS compiler warnings:                                                   0
          `dotnet format whitespace --verify-no-changes`: clean.
          0 per-case suppressions.
          0 documented exceptions.

          Analyzer-rule configuration (in .editorconfig — these are deliberate
          conventions, NOT blanket suppressions of real findings):
            - CA1822 scoped to non-public members (api_surface = private, internal):
              public static-vs-instance is an API-design decision, not a perf bug.
            - CA1707 off for test projects: the Method_Scenario_Result naming convention
              uses underscores by design.
            - CA1711 off for iOS/MacCatalyst platform glue: the framework mandates the
              AppDelegate type name (reserved "Delegate" suffix cannot be changed).

          Rollout: 154 unique analyzer findings at start → 0.
            - 98 CA1707 (test method names) cleared by the test-project editorconfig rule.
            - 5 CA1707 + 5 CA1401 cleared by making Claude.Core interop internal.
            - ProfileCollection → ProfileConfiguration (CA1711; it is a config root).
            - ~40 real code fixes (CA1305/1310/1311/1304/1866 culture; CA1869 cached
              JsonSerializerOptions; CA1859 concrete return; CA1822 static helpers +
              StartupService→static class; CA1852 sealed; CA1805 redundant init; CA2020
              checked IntPtr casts; CA2101 marshaling; CA1838/CA1806 screenshot P/Invoke;
              CS8425 EnumeratorCancellation; CS0067 unused event removed; CS0649 Windows-
              only field #if-scoped; MSTEST0001 explicit DoNotParallelize).

Lint (C++ — DisplayManagerNative): 0 findings.
          clang-format 22.1.0 (Microsoft style): 0 drift on .cpp/.h (vendored json.hpp
          excluded via .clang-format-ignore).
          cppcheck 2.19.0 (--enable=all, json.hpp + system-include noise suppressed): 0
          findings after fixes — deleted dead Utf8ToWide; const-ref locals
          (sourceMode/targetMode/w); reduced monitorPath scope; path.substr(0,n)→resize(n);
          raw '#'→'\\' loop → std::replace; count()+insert() → insert().second.
          Native DLL rebuilds clean (MSBuild x64). cppcheck CI job (lint-cpp) wired.

AI-slop gate (aislop): READY-BUT-GATED.
          aislop's C# engine has not shipped (2026-05-29) — it false-greens on C#. Config
          (.aislop/config.yml, failBelow=80) + a commented-out CI job are in place; flip
          on (uncomment CI job + install the per-edit hook, pinned version) once aislop
          ships C# support. Roslyn/Roslynator remain the real C# gate until then.

CI gate (.gitea/workflows/ci.yml):
          - test:     dotnet build + test Claude.Core.Tests (existing).
          - lint:     dotnet format whitespace --verify-no-changes (whole solution) +
                      dotnet build -warnaserror on the dotnet-buildable managed projects
                      (Claude.Core[.Tests], ClaudeSessionsCLI, ClaudeHookBridge).
          - lint-cpp: cppcheck on DisplayManagerNative.cpp.
          NOTE: DisplayManager.Core, DisplayManagerCLI and MegaSchoen reference the native
          vcxproj / are MAUI, so they need MSBuild + the C++/MAUI toolchain and are gated
          LOCALLY via `MSBuild MegaSchoen.sln -warnaserror` (per CLAUDE.md), not on the
          dotnet-only CI runner. They are verified clean locally (see above).

On-save hook (.claude/settings.json):
          PostToolUse (Write|Edit) runs `dotnet format whitespace` on the edited .cs file,
          resolving the solution via $CLAUDE_PROJECT_DIR (no hard-coded path).
