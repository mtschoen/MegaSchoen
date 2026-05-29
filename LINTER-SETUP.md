# LINTER-SETUP.md — MegaSchoen

Recommended linting setup for MegaSchoen — fleet survey 2026-05-29.

---

## Current state

| Item | Status |
|---|---|
| **Languages** | C# (primary — ~50 `.cs` files across `Claude.Core`, `Claude.Core.Tests`, `ClaudeHookBridge`, `ClaudeSessionsCLI`, `DisplayManager.Core`, `DisplayManager.Core.Tests`, `DisplayManagerCLI`, `MegaSchoen`, `MegaSchoen.UITests`) + C++ (`DisplayManagerNative/DisplayManagerNative.cpp` + `.h`) |
| **C++ vendor header** | `DisplayManagerNative/json.hpp` — nlohmann/json, exclude from linting |
| **`.editorconfig`** | Present and comprehensive — full C# style/naming/formatting rules, all at `suggestion` severity |
| **`Directory.Build.props`** | None |
| **Roslyn analyzers in csproj** | None — no `EnableNETAnalyzers`, `AnalysisLevel`, or analyzer `PackageReference` in any `.csproj` |
| **CI workflows** | None (no `.gitea/workflows/` or `.github/workflows/`) |
| **Claude Code on-save hook** | None (no `.claude/settings*.json`) |
| **Pre-commit config** | None |

**Key observation:** `.editorconfig` rules are all `:suggestion` — meaning `dotnet format` will enforce whitespace/style, but there is no Roslyn analyzer enforcement yet. No CI gate exists.

---

## Three-tier recommendation

### C# (primary)

| Tier | Tool | Command | Why |
|---|---|---|---|
| ① On-save | `dotnet format` | `dotnet format MegaSchoen.sln --include <file>` | Applies `.editorconfig` style/whitespace fixes per-file instantly |
| ② Validate | Roslyn analyzers via `dotnet build -warnaserror` | `dotnet build MegaSchoen.sln -warnaserror` | SDK-bundled analyzers enforce correctness; zero warnings = authoritative gate |
| ② Validate (deep) | Roslynator | Add `<PackageReference Include="Roslynator.Analyzers" Version="4.*" />` to `Directory.Build.props`, then `dotnet build -warnaserror` | 500+ additional rules; runs as part of the normal build |
| ② Validate (solution-wide) | JetBrains InspectCode (`jbinspect`) | `jb inspectcode MegaSchoen.sln --output=report.xml` | Whole-solution, Rider-grade; slow → validate/CI only, not on-save |
| ③ CI | `dotnet format --verify-no-changes` + `dotnet build -warnaserror` + jbinspect | See CI snippet below | Blocks merges on format drift and new warnings |

Tiers ① and ② use different modes of the same build toolchain. `dotnet format` is the fast per-file formatter; `dotnet build -warnaserror` is the authoritative full check. They are complementary, not duplicates.

**Enabling Roslyn analyzers** (required for tier ② to have teeth — currently absent):

Add a `Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-Recommended</AnalysisLevel>
  </PropertyGroup>
</Project>
```

This applies to all projects in the solution without touching individual `.csproj` files.

**Roslynator** (optional deep tier ②):

```xml
<!-- In Directory.Build.props, inside the same <ItemGroup> -->
<PackageReference Include="Roslynator.Analyzers" Version="4.*">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

---

### C++ (`DisplayManagerNative/`)

`DisplayManagerNative` is a Windows-only native DLL (`DisplayManagerNative.cpp` + `.h`). One vendor file (`json.hpp`) should be excluded from all linting.

| Tier | Tool | Command | Why |
|---|---|---|---|
| ① On-save | `clang-format -i <file>` | `clang-format -i DisplayManagerNative\DisplayManagerNative.cpp` | Instant formatting from `.clang-format` config (create one, or pass `-style=Microsoft` for Win32 code) |
| ② Validate | `cppcheck` | `cppcheck --enable=all --suppress=missingIncludeSystem DisplayManagerNative\DisplayManagerNative.cpp` | Zero-false-positive real-bug detection; no compile DB needed |
| ② Validate (deep) | `clang-tidy` | Requires `compile_commands.json` — build with `-DCMAKE_EXPORT_COMPILE_COMMANDS=ON` in a CMake wrapper, or generate via `clang-cl` flags | Style/modernize/bugprone; complementary to cppcheck |
| ③ CI | `clang-format --dry-run --Werror` + `cppcheck` | See CI snippet below | Format drift + real bug gate |

**Note on scope:** The C++ component is a single translation unit (~one `.cpp` file). Tier ② with `cppcheck` alone is a practical minimum; `clang-tidy` adds value if a `compile_commands.json` can be generated from the VS project.

**Exclude vendor header from linting** — pass `--suppress` to cppcheck and add `json.hpp` to `.clang-tidy` `HeaderFilterRegex` or a per-file suppression.

---

## On-save hook (C# — Claude Code `PostToolUse`)

The on-save hook for C# runs `dotnet format` on each edited file. Because `dotnet format` needs a project or solution reference, it is slower than a Rust-based formatter — expect 3–8 seconds. The alternative is to rely on IDE-live Roslyn analyzers (Rider or VS) for instant in-editor feedback and skip the hook.

To enable the hook, paste this into `.claude/settings.json` (or `.claude/settings.local.json`) under the repo root, creating the file if it does not exist:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "f=$(jq -r '.tool_input.file_path // .tool_response.filePath // empty'); case \"$f\" in *.cs) o=$(dotnet format \"C:/Users/mtsch/MegaSchoen/MegaSchoen.sln\" --include \"$f\" 2>&1); [ -n \"$o\" ] && jq -n --arg c \"dotnet format:\\n$o\" '{hookSpecificOutput:{hookEventName:\"PostToolUse\",additionalContext:$c}}';; esac; exit 0"
          }
        ]
      }
    ]
  }
}
```

**Caution:** The solution path in the hook command above is a local absolute path — if you move the repo or run on a second machine, update it. Better: use a path relative to the file being edited, or omit the hook and rely on Rider/VS live analyzers.

---

## CI step (Gitea Actions)

No CI exists yet. When you add it, create `.gitea/workflows/lint.yml`:

```yaml
name: lint

on:
  push:
    branches: [main]
  pull_request:

jobs:
  lint-csharp:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Restore
        run: dotnet restore MegaSchoen.sln

      - name: Format check
        run: dotnet format MegaSchoen.sln --verify-no-changes

      - name: Build (warnings as errors)
        run: dotnet build MegaSchoen.sln -warnaserror --no-restore

  lint-cpp:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install cppcheck
        run: choco install cppcheck -y

      - name: cppcheck
        run: >
          cppcheck --enable=all --suppress=missingIncludeSystem --error-exitcode=1
          DisplayManagerNative/DisplayManagerNative.cpp
```

**Notes:**
- `dotnet format --verify-no-changes` fails the build if any file would be reformatted — catches formatting drift without auto-fixing.
- The MAUI project (`MegaSchoen.csproj`) targets multiple platforms; the CI `dotnet build` step may need `--framework net10.0-windows10.0.26100.0` or a filter to avoid Android/iOS SDK failures on a Windows runner.
- jbinspect is a slow step (~minutes); add it as a separate non-blocking job or a scheduled run rather than on every PR.

---

## Rollout

The recommended path (same as projdash used in PRs #113 / #115 / #116):

1. **Autofix sweep** — run `dotnet format MegaSchoen.sln` once to apply whitespace/style normalization; commit as a single formatting PR.
2. **Enable analyzers + hand-fix** — add `Directory.Build.props` with `EnableNETAnalyzers` + `AnalysisLevel`, run `dotnet build -warnaserror`, work through the real findings.
3. **Bake the gate** — add the CI workflow and the on-save hook.

Whether to do the autofix as a single PR or interleaved with the real fixes is up to you — the projdash 3-PR split (autofix / real-fixes / CI gate) keeps review diffs clean.

**C++** is a single translation unit; a one-off `cppcheck` run + fix is likely a single small PR.
