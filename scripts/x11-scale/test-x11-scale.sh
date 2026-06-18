#!/usr/bin/env bash
# Unit tests for x11-scale derivation logic. Runs anywhere bash + awk exist.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
fail=0
check() { # check <label> <actual> <expected>
  if [ "$2" = "$3" ]; then echo "ok   - $1"; else echo "FAIL - $1: got '$2' want '$3'"; fail=1; fi
}

# --- applier derivation ---
# Source apply.sh with a fake DISPLAY so _x11_apply_scale runs, and stub xrdb.
export DISPLAY=":0"
PATH="$here/.testbin:$PATH"; mkdir -p "$here/.testbin"
printf '#!/bin/sh\ncat >/dev/null\n' > "$here/.testbin/xrdb"; chmod +x "$here/.testbin/xrdb"
# shellcheck disable=SC1091
X11_SCALE=2 . "$here/apply.sh"
check "2: GDK_SCALE"        "$GDK_SCALE"                    "2"
check "2: GDK_DPI_SCALE"    "$GDK_DPI_SCALE"                "0.5000"
check "2: QT_SCALE_FACTOR"  "$QT_SCALE_FACTOR"              "2"
check "2: QT_FONT_DPI"      "$QT_FONT_DPI"                  "96"
check "2: ELM_SCALE"        "$ELM_SCALE"                    "2"
check "2: XCURSOR_SIZE"     "$XCURSOR_SIZE"                 "48"
check "2: STEAM scaling"    "$STEAM_FORCE_DESKTOPUI_SCALING" "2"

# fractional 1.25 -> integer GTK UI 1, fractional fonts/Qt/Steam
X11_SCALE="1.25" _x11_apply_scale
check "1.25: GDK_SCALE"     "$GDK_SCALE"                    "1"
check "1.25: GDK_DPI_SCALE" "$GDK_DPI_SCALE"                "1.0000"
check "1.25: QT_SCALE"      "$QT_SCALE_FACTOR"              "1.25"
check "1.25: XCURSOR_SIZE"  "$XCURSOR_SIZE"                 "30"
check "1.25: STEAM"         "$STEAM_FORCE_DESKTOPUI_SCALING" "1.25"

# fractional 1.75 -> GTK UI rounds to 2
X11_SCALE="1.75" _x11_apply_scale
check "1.75: GDK_SCALE"     "$GDK_SCALE"                    "2"
check "1.75: GDK_DPI_SCALE" "$GDK_DPI_SCALE"                "0.5000"
check "1.75: XCURSOR_SIZE"  "$XCURSOR_SIZE"                 "42"

# invalid scale falls back to 1
X11_SCALE="garbage" _x11_apply_scale
check "garbage->1 GDK"     "$GDK_SCALE"        "1"

# --- sshx fractional detection (DPI/96, unrounded) ---
frac() { awk -v d="$1" 'BEGIN{printf "%.4f", d/96.0}'; }
check "192->2.0"  "$(frac 192)" "2.0000"
check "168->1.75" "$(frac 168)" "1.7500"
check "120->1.25" "$(frac 120)" "1.2500"
check "96->1.0"   "$(frac 96)"  "1.0000"

# --- sshx arg-splitting + login-shell wrapping ---
# Stub ssh prints each received arg on its own line; inject via $SSHX_SSH.
stub="$here/.testbin/ssh-stub"
printf '#!/usr/bin/env bash\nfor a in "$@"; do printf "%%s\\n" "$a"; done\n' > "$stub"; chmod +x "$stub"
export SSHX_SSH="$stub"
# shellcheck disable=SC1091
. "$here/sshx.sh"

out="$(sshx 2 -X llamabox steam)"
check "wrap: -o"       "$(printf '%s\n' "$out" | sed -n 1p)" "-o"
check "wrap: scale"    "$(printf '%s\n' "$out" | sed -n 2p)" "SetEnv=X11_SCALE=2"
check "wrap: -X"       "$(printf '%s\n' "$out" | sed -n 3p)" "-X"
check "wrap: host"     "$(printf '%s\n' "$out" | sed -n 4p)" "llamabox"
check "wrap: login cmd" "$(printf '%s\n' "$out" | sed -n 5p)" "bash -lc 'steam '"

out="$(sshx 1 -X llamabox)"
check "nocmd: arg count" "$(printf '%s\n' "$out" | grep -c .)" "4"
check "nocmd: host last"  "$(printf '%s\n' "$out" | sed -n 4p)" "llamabox"

out="$(sshx 3 -p 2222 llamabox echo hi)"
check "valopt: -p"     "$(printf '%s\n' "$out" | sed -n 3p)" "-p"
check "valopt: port"   "$(printf '%s\n' "$out" | sed -n 4p)" "2222"
check "valopt: host"   "$(printf '%s\n' "$out" | sed -n 5p)" "llamabox"
check "valopt: cmd"    "$(printf '%s\n' "$out" | sed -n 6p)" "bash -lc 'echo hi '"

rm -rf "$here/.testbin"
exit $fail
