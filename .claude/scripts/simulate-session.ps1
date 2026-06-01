#requires -Version 7
# End-to-end replay: pipe each fixture step into the REAL ClaudeHookBridge.exe,
# then assert ClaudeSessionsCLI list --json reports the expected State.
# Usage: pwsh .claude/scripts/simulate-session.ps1
#
# Slug encoding mirrors Claude.Core.SlugEncoder EXACTLY: trim trailing \ and /,
# then replace each of ':', '\', '/' with '-'. (NOT a broad non-alphanumeric
# replace — the plan's original '[^a-zA-Z0-9]' regex was wrong.)
# Correction: bridge now honors MEGASCHOEN_STATE_DIR (was hardcoded to default).
[CmdletBinding()]
param(
    [string]$FixturesDir = "$PSScriptRoot/../../Claude.Core.Tests/Fixtures/sessions",
    [string]$Bridge = "$PSScriptRoot/../../ClaudeHookBridge/bin/Debug/net10.0-windows10.0.26100.0/ClaudeHookBridge.exe",
    [string]$Cli = "$PSScriptRoot/../../ClaudeSessionsCLI/bin/Debug/net10.0-windows10.0.26100.0/ClaudeSessionsCLI.exe"
)

$ErrorActionPreference = 'Stop'
foreach ($exe in @($Bridge, $Cli)) {
    if (-not (Test-Path $exe)) { throw "Missing binary: $exe (build the solution first)" }
}

# Mirror Claude.Core.SlugEncoder EXACTLY: trim trailing \ and /, then replace
# each of ':' '\' '/' with '-'. (NOT a broad non-alphanumeric replace.)
function ConvertTo-Slug([string]$cwd) {
    $trimmed = $cwd.TrimEnd('\', '/')
    return ($trimmed -replace '[:\\/]', '-')
}

$projectsRoot = Join-Path $HOME ".claude/projects"
$failures = 0
$scenarioFiles = Get-ChildItem -Path $FixturesDir -Filter *.json | Sort-Object Name

# Outer try/finally guarantees the env vars (which point at a throwaway state
# dir) never leak into the caller's shell, even if a step throws under
# $ErrorActionPreference='Stop'. A leaked MEGASCHOEN_STATE_DIR pointed at a
# deleted dir would silently make the user's later manual ClaudeSessionsCLI runs
# show zero sessions until they restart their shell.
try {
    foreach ($file in $scenarioFiles) {
        $scenario = Get-Content $file.FullName -Raw | ConvertFrom-Json
        $stateDir = Join-Path ([System.IO.Path]::GetTempPath()) "replay-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $stateDir | Out-Null

        $slug = ConvertTo-Slug $scenario.cwd
        $slugDir = Join-Path $projectsRoot $slug
        New-Item -ItemType Directory -Path $slugDir -Force | Out-Null
        $transcript = Join-Path $slugDir "$($scenario.sessionId).jsonl"
        '{"type":"assistant","message":{},"cwd":"' + ($scenario.cwd -replace '\\', '\\\\') + '"}' | Set-Content $transcript

        $env:MEGASCHOEN_STATE_DIR = $stateDir
        $env:MEGASCHOEN_FAKE_PROCESSES = (ConvertTo-Json -Compress @(@{ cwd = $scenario.cwd; count = 1 }))

        # Per-scenario try/finally: the temp state dir + the fake transcript under
        # the real ~/.claude/projects/ are always removed even if a step throws.
        try {
            Write-Host "`n=== $($scenario.name) ===" -ForegroundColor Cyan
            for ($i = 0; $i -lt $scenario.steps.Count; $i++) {
                $step = $scenario.steps[$i]
                $payload = @{
                    session_id      = $scenario.sessionId
                    cwd             = $scenario.cwd
                    transcript_path = $transcript
                    hook_event_name = $step.event
                }
                if ($step.notificationType) { $payload.notification_type = $step.notificationType }
                if ($step.message)          { $payload.message = $step.message }

                ($payload | ConvertTo-Json -Compress) | & $Bridge | Out-Null
                if ($step.delayMs -gt 0) { Start-Sleep -Milliseconds $step.delayMs }

                $json = & $Cli list --json | ConvertFrom-Json
                $row = $json | Where-Object { $_.SessionId -eq $scenario.sessionId } | Select-Object -First 1
                $actual = if ($row) { $row.State } else { "(absent)" }

                if ($actual -eq $step.expectAfter) {
                    Write-Host ("  step {0} {1}/{2} -> {3} OK" -f $i, $step.event, $step.notificationType, $actual) -ForegroundColor Green
                } else {
                    Write-Host ("  step {0} {1}/{2} -> {3} EXPECTED {4}" -f $i, $step.event, $step.notificationType, $actual, $step.expectAfter) -ForegroundColor Red
                    $failures++
                }
            }
        }
        finally {
            Remove-Item -Recurse -Force $stateDir -ErrorAction SilentlyContinue
            Remove-Item -Force $transcript -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Remove-Item Env:MEGASCHOEN_STATE_DIR, Env:MEGASCHOEN_FAKE_PROCESSES -ErrorAction SilentlyContinue
}

if ($failures -gt 0) { Write-Host "`n$failures step(s) FAILED" -ForegroundColor Red; exit 1 }
Write-Host "`nAll replay steps passed" -ForegroundColor Green
