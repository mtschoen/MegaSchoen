MegaSchoen test report - 2026-06-18
===========================================

Status:   PASS
Mode:     close-the-gap (wire up + clear the pr-crew/coverage 80% gate, issue #8)
Tests:    162 total on the coverage TFM (Claude.Core.Tests, net10.0-windows10.0.26100.0)
          The net10.0 (cross-platform) TFM also runs in CI for correctness; it
          carries the Linux-only tests (ProcFileSystem, LinuxClaudeProcessLocator)
          that the Windows TFM #if-excludes.
Git:      feat/coverage-gate-pr-crew (base main bab4bda)

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
  dotnet format whitespace : 0 changes (Claude.Core.Tests, --verify-no-changes)
  Roslyn + Roslynator      : 0 findings (Claude.Core.Tests built -warnaserror, both TFMs)
  MSTest analyzers         : 0 findings (MSTEST0037 Assert.HasCount/IsEmpty applied)
  cppcheck (C++)           : unchanged - no native code touched this change
  aislop (local 0.12.0)    : new/changed files 0 findings. The best-effort empty
                             catches previously noted in the test cleanup were
                             removed (HookCaptureTests) to follow the let-it-throw
                             convention in Fakes/ClaudeProjectsFixture.cs:40 (Dispose
                             guards with Directory.Exists and lets delete failures
                             throw, no catch), per PR review. No empty-catch finding
                             remains. CI gate now pinned to @schoen/aislop@0.12.0
                             (was 0.10.1).

Notes:
  - This change is test-only + CI/build wiring: it adds tests and the coverage
    measurement/posting, and does not alter any feature behavior.
  - Local re-measurement: pwsh .claude/scripts/measure-coverage.ps1
