#!/usr/bin/env bash
# Build the Linux session binaries on this host, install the Claude hook set,
# and expose `agent-sessions` on PATH. Run on the sessions host:
#   ssh <target> 'bash -s' < scripts/setup-sessions-host.sh
# or from a checkout on that host: ./scripts/setup-sessions-host.sh
set -euo pipefail

REPO="${MEGASCHOEN_REPO:-$HOME/MegaSchoen}"
BIN="$HOME/.local/bin"
PUB="$HOME/.local/share/MegaSchoen/bin"
mkdir -p "$BIN" "$PUB"

cd "$REPO"
git pull --ff-only

# -p:EnableWindowsTargeting=true: these projects multi-target net10.0;net10.0-windows.
# `dotnet publish` restores ALL target frameworks even with -f, and restoring the
# Windows TFM on Linux trips NETSDK1100 without this flag. -f net10.0 still ensures
# only the Linux framework is actually built/published; the Windows TFM is restore-only.
dotnet publish ClaudeHookBridge/ClaudeHookBridge.csproj   -f net10.0 -p:EnableWindowsTargeting=true -c Release -r linux-x64 --self-contained false -o "$PUB/hookbridge"
dotnet publish AgentSessionsCLI/AgentSessionsCLI.csproj -f net10.0 -p:EnableWindowsTargeting=true -c Release -r linux-x64 --self-contained false -o "$PUB/sessions"

ln -sf "$PUB/sessions/AgentSessionsCLI" "$BIN/agent-sessions"
ln -sf "$PUB/sessions/AgentSessionsCLI" "$BIN/claude-sessions"

# Register the hook set (Notification/Stop/UserPromptSubmit/SessionEnd/PostToolUse)
"$PUB/hookbridge/ClaudeHookBridge" install

echo "Done. Ensure $BIN is on PATH for non-interactive SSH (e.g. add to ~/.bashrc / ~/.profile)."
echo "Verify: agent-sessions list --json"
