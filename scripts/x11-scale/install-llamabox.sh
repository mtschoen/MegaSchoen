#!/usr/bin/env bash
# Idempotent server-side installer for x11-scale on llamabox (Arch Linux).
# Run ON llamabox after apply.sh has been copied to ~/.config/x11-scale/apply.sh.
#
# Why above the interactive guard: ~/.bashrc has `[[ $- != *i* ]] && return`.
# Interactive `ssh -X` sessions source ~/.bashrc and pass the guard, but a
# one-shot remote command only sources ~/.bashrc when run as a login shell
# (`bash -lc`), and a login shell is non-interactive so it hits the guard and
# returns. Placing the source line above the guard makes the applier run in
# both cases. (Mirrors the existing superpowers block placement.)
set -euo pipefail

# 1. xrdb provides Xft.dpi on the X server (font DPI for legacy X apps).
command -v xrdb >/dev/null 2>&1 || sudo pacman -S --noconfirm xorg-xrdb

# 2. Source apply.sh above the interactive guard in ~/.bashrc (idempotent).
bashrc="$HOME/.bashrc"
if ! grep -q 'x11-scale/apply.sh' "$bashrc"; then
    cp "$bashrc" "$bashrc.x11bak.$$"
    tmp="$(mktemp)"
    awk '
        !ins && /\[\[ \$- != \*i\* \]\] && return/ {
            print "# x11-scale: DPI for forwarded GUI apps (above interactive guard)"
            print "[ -f \"$HOME/.config/x11-scale/apply.sh\" ] && . \"$HOME/.config/x11-scale/apply.sh\""
            print ""
            ins = 1
        }
        { print }
        END {
            if (!ins) {
                print "# x11-scale: DPI for forwarded GUI apps"
                print "[ -f \"$HOME/.config/x11-scale/apply.sh\" ] && . \"$HOME/.config/x11-scale/apply.sh\""
            }
        }
    ' "$bashrc" > "$tmp"
    mv "$tmp" "$bashrc"
fi

# 3. sshd AcceptEnv drop-in so `ssh -o SetEnv=X11_SCALE=N` is honored.
conf=/etc/ssh/sshd_config.d/20-x11scale.conf
if ! sudo grep -qs 'AcceptEnv X11_SCALE' "$conf"; then
    echo 'AcceptEnv X11_SCALE' | sudo tee "$conf" >/dev/null
fi
sudo sshd -t
sudo systemctl reload sshd

echo "x11-scale llamabox install complete"
