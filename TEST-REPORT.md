MegaSchoen test report — 2026-07-28T12:58:12-07:00
=================================================

Status:   PASS
Tests:    255 Claude.Core.Tests + 3 MegaSchoen.Avalonia.Tests, all passing
Git:      97fd729 (issue #31 Phase 1; report includes final working-tree tests)
Coverage: Claude.Core 944/1067 lines (88.47%), branch 83.67%,
          method 88.61% on the cross-platform net10.0 scope
          ActiveSessionEnumerator coordinator 10/10 lines (100%)
          ClaudeSessionSource 182/185 lines (98.38%)

Scope:
  - Issue #31 Phase 1 adds the provider-neutral ISessionSource seam, keeps
    ClaudeSessionSource as the only configured provider, and renames the
    sessions CLI and host setup entry points.
  - Coverage was measured with coverlet.msbuild on the net10.0 TFM available
    in this Linux worktree. The configured pr-crew threshold is 80%.
  - CI measures the Windows TFM and merges the separately measured Avalonia
    scope. The last Windows baseline recorded on main was 93.06% for
    Claude.Core.

Prior recorded scopes (not rerun because issue #31 does not touch them):
  - DisplayManager.Core merged scope: 1036/1242 lines (83.41%) and 230/310
    branches.
  - DisplayManager.Core DisplayApplyGate: 30/30 lines, 6/6 branches, and 3/3
    methods (100%).

Verification:
  - `dotnet build Claude.Core.Tests/Claude.Core.Tests.csproj -c Release
    --no-restore -warnaserror`: 0 warnings, 0 errors.
  - `dotnet build AgentSessionsCLI/AgentSessionsCLI.csproj -f net10.0
    -c Release --no-restore -warnaserror`: 0 warnings, 0 errors.
  - `dotnet build MegaSchoen.Avalonia/MegaSchoen.Avalonia.csproj -c Release
    -warnaserror`: 0 warnings, 0 errors.
  - `dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj -f net10.0
    -c Release /p:CollectCoverage=true`: 255 passed.
  - `dotnet test MegaSchoen.Avalonia.Tests/MegaSchoen.Avalonia.Tests.csproj
    -c Release`: 3 passed.
  - Built `AgentSessionsCLI` was invoked with no arguments and with isolated
    `list --json`; usage names `agent-sessions`, and the isolated snapshot was
    an empty JSON array.
  - `scripts/setup-sessions-host.sh` passed `bash -n` and a disposable fake
    publish smoke. Both installed launchers (`agent-sessions` and the
    compatibility `claude-sessions`) invoked the built CLI successfully.
  - `aislop scan .` and `aislop ci .` reported 0 lint, code-quality, AI-slop,
    or security findings.

Platform limits:
  - The Linux host cannot restore the full solution because the Windows MAUI
    project requires an unavailable MAUI Tizen workload (`NETSDK1147`).
  - `.editorconfig` mandates CRLF while this Linux checkout stores C# as LF.
    The formatter and aislop therefore report the documented whole-tree set of
    196 C# formatting warnings. No whole-tree line-ending rewrite was applied;
    Windows CI checks out CRLF and remains authoritative for formatting.
