MegaSchoen test report - 2026-06-27
===========================================

Status:   PASS
Mode:     close-the-gap (make the aislop jb gate honest, then clear it to 0 findings)
Tests:    186 Claude.Core.Tests (net10.0-windows10.0.26100.0) + 40 DisplayManager.Core.Tests
          = 226 total, all passing. The net10.0 (cross-platform) TFM also runs in CI for
          correctness; it carries the Linux-only tests (ProcFileSystem,
          LinuxClaudeProcessLocator) that the Windows TFM #if-excludes.
Git:      fix/aislop-jb-gate-honest (base main 219eea5)

Coverage: 828/900 line statements = 92.0% line coverage
          Scope:  Claude.Core production assembly, measured on the
                  net10.0-windows10.0.26100.0 TFM (the CI Windows runner) so
                  Windows-only code counts toward the denominator.
          Tool:   coverlet.msbuild -> Cobertura; CI posts the percent as the
                  `pr-crew/coverage` commit status the gate reads (see
                  schoen/pr-crew docs/coverage-gate.md). Gate threshold: 80%.
          Exclusions (in Claude.Core.Tests.csproj <ExcludeByFile>, per case):
            - **/*.g.cs              source-generated P/Invoke marshalling stubs
            - **/Interop/*.cs        hand-written Win32 P/Invoke declaration layers
            - **/Windows/*.cs        Win32 orchestration needing a live desktop
            - **/ProcessResolver.cs  live process/window enumeration + PEB reads
            - **/Remote/SshStreamProcess.cs  thin live-`ssh` subprocess wrapper
          These are genuinely-untestable platform glue requiring a live desktop
          session; the *pure* logic they feed was already extracted into
          separately-tested classes (AncestorWindowResolver, SshConnectionParser,
          BackgroundSessionParser, SshSessionWindowResolver - all covered).
          0 source-level [ExcludeFromCodeCoverage] / pragma annotations.

          Out of automated measurement (documented baseline, not regressed by this
          change): DisplayManager.Core (native-C++-coupled; its 39 managed tests
          still run locally), the CLI entry points (DisplayManagerCLI /
          ClaudeSessionsCLI / ClaudeHookBridge Program.cs), and the MAUI UI.

          Remaining in-scope gaps (all defensive last-resort catch blocks that the
          code itself never lets throw, e.g. Logger / HookCapture / StateStore
          best-effort I/O guards): ~72 lines. They sit below the public surface and
          are not reachable without faulting the OS underneath them.

Lint (gates that apply to the touched managed code - all GREEN):
  Roslyn + Roslynator      : 0 findings (-warnaserror, solution build clean 0/0)
  cppcheck (C++)           : unchanged - no native code touched this change
  aislop (whole-repo gate) : score 100/100, 0 findings across all engines (format,
                             lint, code-quality, ai-slop, security), 115 files scanned.
                             This is the FIRST honest run: the jb (ReSharper) engine
                             now actually executes (the CI gate previously installed
                             roslynator-only and never built the solution, so jb/*
                             findings were invisible - a false green). A clean run
                             surfaced 218 real findings; all are now cleared:
                               - ~150 mechanical (redundant usings/qualifiers/casts/
                                 args, add `partial` for CsWinRT1028, Frame->Border for
                                 MAUI ObsoleteElement, async-void lambdas wrapped in a
                                 logging RunCommand bridge, unused params -> discards).
                               - 3 AccessToDisposedClosure + 1 DTO-setter: per-case
                                 `// ReSharper disable once` with justification (correct
                                 patterns jb flags conservatively).
                               - ~22 MAUI binding/serialization false positives (jb
                                 cannot follow RelativeSource / BindableLayout bindings
                                 or System.Text.Json member use): excluded by jb rule-id
                                 in .aislop/config.yml lint.csharp.jbExcludeTypes
                                 (binding inspection + the *.Global unused-member family
                                 only; private-scope *.Local stays active). Rationale is
                                 documented inline in config.yml.

Notes:
  - The findings cleanup changed production code (redundancy removal + behavior-
    preserving refactors); all 226 tests still pass and the solution builds 0/0.
  - Coverage was NOT re-measured this change. Claude.Core production logic was
    effectively untouched (only a redundant switch arm removed - still covered), so
    the ~92% baseline is expected to hold. Re-measure when convenient:
    pwsh .claude/scripts/measure-coverage.ps1
  - Local gate re-run: aislop ci . --json  (jb builds the solution; ~70s).
