# CI repair blockers

- Can the three jobs be rerun after outbound DNS/network access is restored on
  `llamabox-windows`? The staged logs show both C# jobs and the C++ job stopped
  before any repository command ran: two while cloning `actions/checkout@v4`
  from GitHub, and one while `actions/setup-dotnet@v4` resolved
  `builds.dotnet.microsoft.com`.
- If these jobs must run without public network access, what are the exact
  Gitea-local action URLs and refs for the approved `checkout` and
  `setup-dotnet` mirrors? Replacing those bootstrap actions without known
  internal mirrors would make checkout and SDK provisioning speculative.
- Is the .NET 10 SDK guaranteed to be installed and on `PATH` for every
  `windows-latest` runner? If so, may `actions/setup-dotnet@v4` be removed from
  the three managed-code jobs instead of replaced?
- Which cppcheck version is provisioned on the runner? The branch now relies on
  that installation, so the version is needed to reproduce the exact analyzer
  rule set deterministically.
- Which local aislop version is authoritative? `AGENTS.md` says the installed
  fork is pinned, but this worktree resolves `aislop 0.14.0` while CI pins
  `@schoen/aislop@0.10.1`; the newer local ruleset reports pre-existing
  whole-repository findings and cannot reproduce the pinned CI gate.
- What is the approved Windows C++ coverage command and baseline for
  `DisplayManagerNative`? `TEST-REPORT.md` currently records that line coverage
  is not instrumented, so native coverage cannot be verified from the
  repository's documented tooling.
