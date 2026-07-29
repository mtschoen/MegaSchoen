MegaSchoen test report — 2026-07-28T20:06:32-07:00
=================================================

Status:   PASS (available Linux verification)
Tests:    61 DisplayManager.Core.Tests + 31 MegaSchoen.Avalonia.Tests, all passing
Git:      3badce7 (issue #60 final working tree)
Coverage: MegaSchoen.Avalonia lifecycle scope 174/177 lines (98.30%),
          212/214 branches (99.06%), and 100% methods

Scope:
  - Issue #60 adds Avalonia's Windows profile hotkeys, HKCU login
    autostart, single-instance activation, and x64/native packaging contract.
  - The measured Avalonia scope covers the platform-neutral controllers,
    gesture translation, display-profile dispatch, startup commands, and
    packaging verifier. Live registry/mutex/Win32 message-window bindings are
    validated by warning-as-error compilation and the Windows package smoke.
  - CI continues to merge this report with Claude.Core's Windows coverage;
    the configured pr-crew threshold is 80%.

Prior recorded scopes (not rerun because issue #60 does not change them):
  - Claude.Core: 944/1067 lines (88.47%) on net10.0; the last recorded
    Windows baseline was 93.06%.
  - DisplayManager.Core merged scope: 1036/1242 lines (83.41%) and 230/310
    branches.

Verification:
  - `dotnet test DisplayManager.Core.Tests/DisplayManager.Core.Tests.csproj
    -c Release`: 61 passed.
  - `dotnet test MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release -warnaserror /p:CollectCoverage=true`: 31 passed; 98.30% line,
    99.06% branch, and 100% method coverage in the configured lifecycle scope.
  - `dotnet build MegaSchoen.Avalonia/MegaSchoen.Avalonia.csproj -c Release
    -r win-x64 -warnaserror`: 0 warnings, 0 errors; `file` identifies the
    produced apphost as PE32+ x86-64.
  - The built Linux apphost returned 0 for `--version` and returned 1 with
    the expected platform diagnostic for `--verify-native`.
  - `.gitea/workflows/ci.yml` parses as YAML and adds an authoritative Windows
    Release solution build that requires the x64 output path, requires
    `DisplayManagerNative.dll` beside the Avalonia executable, and runs the
    executable's real display-query P/Invoke smoke.
  - `git diff --check` is clean.
  - `aislop scan .` and `aislop ci .` report 0 lint, code-quality, AI-slop,
    or security findings.

Platform limits:
  - This Linux host cannot run Visual Studio 18 MSBuild or launch the Windows
    Release app. The new `package-windows` CI job is the authoritative
    Release/native runtime validation.
  - `.editorconfig` mandates CRLF while git stores LF on this checkout.
    `dotnet format whitespace MegaSchoen.sln --verify-no-changes` and aislop
    therefore reproduce the documented whole-tree line-ending flood (212 C#
    files in the current aislop scan). No whole-tree rewrite was applied;
    Windows checkout/CI remains authoritative for formatting.
