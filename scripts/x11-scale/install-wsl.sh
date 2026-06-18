#!/usr/bin/env bash
# Idempotent installer for the chonkers (WSL) side of x11-scale.
# Run from inside WSL: bash install-wsl.sh
#
# The DPI helper goes on the WINDOWS filesystem (%USERPROFILE%\.x11-scale)
# because powershell.exe -File cannot read a \\wsl$ path. sshx.sh goes in the
# WSL ~/bin and is sourced from ~/.bashrc for interactive shells.
set -euo pipefail
src="$(cd "$(dirname "$0")" && pwd)"

# 1. Resolve the Windows home and copy the helper there.
# shellcheck disable=SC2016  # $env:USERPROFILE is PowerShell, must not expand in bash
win_home_w="$(powershell.exe -NoProfile -Command '[Console]::Write($env:USERPROFILE)' 2>/dev/null | tr -d '\r')"
[ -n "$win_home_w" ] || { echo "install-wsl: could not resolve %USERPROFILE%" >&2; exit 1; }
helper_dir_u="$(wslpath -u "$win_home_w")/.x11-scale"
mkdir -p "$helper_dir_u"
cp "$src/win-monitor-dpi.ps1" "$helper_dir_u/win-monitor-dpi.ps1"

# 2. Install sshx.sh into ~/bin and source it from ~/.bashrc (idempotent).
mkdir -p "$HOME/bin"
cp "$src/sshx.sh" "$HOME/bin/sshx.sh"
bashrc="$HOME/.bashrc"
if ! grep -q 'bin/sshx.sh' "$bashrc"; then
    {
        printf '\n# sshx: DPI-aware ssh for forwarded GUI apps\n'
        # shellcheck disable=SC2016  # $HOME must stay literal in the written .bashrc line
        printf '[ -f "$HOME/bin/sshx.sh" ] && . "$HOME/bin/sshx.sh"\n'
    } >> "$bashrc"
fi

echo "sshx installed: helper at ${win_home_w}\\.x11-scale\\win-monitor-dpi.ps1"
