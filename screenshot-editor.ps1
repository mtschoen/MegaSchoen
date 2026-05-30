#!/usr/bin/env pwsh
# Layout Editor screenshot pipeline.
#
# Builds MegaSchoen, launches it straight into the Layout Editor (App reads the
# --screenshot-editor flag and opens the editor as its sole window), finds that window,
# and captures it to editor-screenshot.png at the repo root via PrintWindow.
#
# The Win32 capture (PrintWindow with PW_RENDERFULLCONTENT + DPI scaling) is the proven
# MAUI/WinUI3 approach — WinUI windows can't be rendered headlessly the way Avalonia can,
# so we screenshot the real on-screen window of the running app.
#
#   ./screenshot-editor.ps1            # build, launch, capture
#   ./screenshot-editor.ps1 -NoBuild   # skip the build, just capture

param(
    [switch]$NoBuild,
    [string]$OutputPath = "$PSScriptRoot/scratch/editor-screenshot.png",
    [int]$TimeoutSeconds = 40
)

$ErrorActionPreference = "Stop"

# The PrintWindow capture relies on System.Drawing's GDI+ types (Bitmap/Graphics). Under pwsh 7
# those are forwarded to System.Drawing.Common, which isn't shipped — so re-exec under Windows
# PowerShell 5.1 (.NET Framework), where they're native and Add-Type just works.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Value -is [switch]) { if ($kv.Value) { $forward += "-$($kv.Key)" } }
        else { $forward += "-$($kv.Key)"; $forward += "$($kv.Value)" }
    }
    & "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" @forward
    exit $LASTEXITCODE
}

$exePath = Join-Path $PSScriptRoot "MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/MegaSchoen.exe"

function Write-Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }

# A running instance locks MegaSchoen.exe and fails the build's copy step — stop it before
# building (also covers the case where the user left an editor window open).
function Stop-MegaSchoen {
    Get-Process MegaSchoen -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# --- Build -------------------------------------------------------------------
if (-not $NoBuild) {
    Write-Step "Stopping any running MegaSchoen instance (locks the build output)..."
    Stop-MegaSchoen
    Write-Step "Building MegaSchoen (MSBuild)..."
    # VS 18 ships the v145 native toolset + the .NET 10 SDK the solution needs; VS 2022's
    # MSBuild resolves SDK 9.0.x and lacks v145, so it cannot build this solution. Use vswhere
    # -latest (it understands real product versions — the folder name flipped from year-based
    # "2022" to sequential "18", so a numeric folder sort wrongly ranks 2022 above 18). Build the
    # SOLUTION: its platform mappings keep MegaSchoen on x64 / libraries AnyCPU, whereas the bare
    # .csproj fans out to all 4 TFMs and fails.
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuild) { throw "MSBuild.exe not found via vswhere." }
    & $msbuild (Join-Path $PSScriptRoot "MegaSchoen.sln") -p:Configuration=Debug -nologo -verbosity:minimal -m
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path $exePath)) { throw "App not found at $exePath. Build first (drop -NoBuild)." }

# --- Win32 capture helper ----------------------------------------------------
# SingleInstanceService silently swallows a second launch and forwards to the running
# instance (which shows the normal shell, not the editor), so we must kill any running
# MegaSchoen first — see the "SingleInstance Masks Stale-Build Testing" project note.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class EditorShot
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, int flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);

    const int PW_RENDERFULLCONTENT = 0x00000002;

    public static IntPtr FindWindow(int processId, string titleSubstring)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h))
            {
                var sb = new StringBuilder(512);
                GetWindowText(h, sb, sb.Capacity);
                var title = sb.ToString();
                if (title.Length > 0 && title.IndexOf(titleSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string Capture(IntPtr hWnd, string path)
    {
        RECT r; if (!GetWindowRect(hWnd, out r)) throw new Exception("GetWindowRect failed.");
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        uint dpi = GetDpiForWindow(hWnd);
        double scale = dpi / 96.0;
        int bw = (int)(w * scale), bh = (int)(h * scale);

        using (var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
        {
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.PageUnit = GraphicsUnit.Pixel;
                IntPtr hdc = g.GetHdc();
                try { PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }
            bmp.Save(path, ImageFormat.Png);
        }
        return string.Format("{0}x{1} @ {2} DPI", bw, bh, dpi);
    }
}
"@ -ReferencedAssemblies System.Drawing

# --- Relaunch into the editor ------------------------------------------------
Write-Step "Stopping any running MegaSchoen instance..."
Stop-MegaSchoen

Write-Step "Launching editor (--screenshot-editor)..."
$proc = Start-Process -FilePath $exePath -ArgumentList "--screenshot-editor" -PassThru

try {
    Write-Step "Waiting for the editor window..."
    $hWnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { throw "App exited early (code $($proc.ExitCode)) before opening a window. Windows App SDK runtime missing?" }
        $hWnd = [EditorShot]::FindWindow($proc.Id, "Edit Layout")
        if ($hWnd -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($hWnd -eq [IntPtr]::Zero) { throw "Editor window not found within $TimeoutSeconds s." }

    # Let the canvas settle (fit-to-view runs on first SizeChanged).
    Start-Sleep -Seconds 2

    Write-Step "Capturing window..."
    $outputDir = Split-Path -Parent $OutputPath
    if ($outputDir -and -not (Test-Path $outputDir)) { New-Item -ItemType Directory -Path $outputDir -Force | Out-Null }
    $info = [EditorShot]::Capture($hWnd, $OutputPath)
    $kb = [math]::Round((Get-Item $OutputPath).Length / 1KB, 1)
    Write-Host "Saved $OutputPath ($info, $kb KB)" -ForegroundColor Green
}
finally {
    if (-not $proc.HasExited) { $proc | Stop-Process -Force }
}
