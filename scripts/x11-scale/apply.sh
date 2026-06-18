# shellcheck shell=bash
# x11-scale applier - source from ~/.bashrc on llamabox.
# No-op unless $DISPLAY is set (i.e. an X-forwarded session).
# Scale source priority: $X11_SCALE (ssh SetEnv) -> state file -> default 1.
#
# X11_SCALE is a fractional factor (e.g. 1.25, 1.75, 2). Derivation:
#   GDK_SCALE      = nearest integer (GTK UI scales in integer steps only)
#   GDK_DPI_SCALE  = 1 / GDK_SCALE (counters GDK_SCALE's font multiply; fonts
#                    then come from Xft.dpi, the known-good pairing)
#   Xft.dpi        = round(96 * factor) (fractional font DPI; GTK + Xft apps)
#   QT_SCALE_FACTOR, ELM_SCALE, STEAM_FORCE_DESKTOPUI_SCALING = factor (fractional ok)

_x11_scale_state="$HOME/.config/x11-scale/scale"

_x11_apply_scale() {
    [ -n "${DISPLAY:-}" ] || return 0

    local scale="${X11_SCALE:-}"
    if [ -z "$scale" ] && [ -r "$_x11_scale_state" ]; then
        scale="$(cat "$_x11_scale_state" 2>/dev/null)"
    fi
    # Accept a positive number (integer or decimal); otherwise default to 1.
    case "$scale" in ''|*[!0-9.]*|*.*.*) scale=1 ;; esac
    awk "BEGIN{exit !($scale+0 >= 1)}" 2>/dev/null || scale=1

    local gdk
    gdk="$(awk -v f="$scale" 'BEGIN{g=int(f+0.5); if(g<1)g=1; print g}')"
    export GDK_SCALE="$gdk"
    GDK_DPI_SCALE="$(awk -v g="$gdk" 'BEGIN{printf "%.4f", 1.0/g}')"; export GDK_DPI_SCALE
    export QT_SCALE_FACTOR="$scale"
    export QT_FONT_DPI=96
    export QT_AUTO_SCREEN_SCALE_FACTOR=0
    export ELM_SCALE="$scale"
    export STEAM_FORCE_DESKTOPUI_SCALING="$scale"
    XCURSOR_SIZE="$(awk -v f="$scale" 'BEGIN{printf "%d", int(24*f+0.5)}')"; export XCURSOR_SIZE

    if command -v xrdb >/dev/null 2>&1; then
        printf 'Xft.dpi: %s\n' "$(awk -v f="$scale" 'BEGIN{printf "%d", int(96*f+0.5)}')" | xrdb -merge - 2>/dev/null
    fi
}

# xscale <factor>: change scale for apps launched later in THIS shell (e.g. xscale 1.5).
xscale() {
    local n="${1:-}"
    case "$n" in ''|*[!0-9.]*|*.*.*) echo "usage: xscale <factor>=1 (e.g. 1.5)" >&2; return 2 ;; esac
    awk "BEGIN{exit !($n+0 >= 1)}" 2>/dev/null || { echo "xscale: factor must be >= 1" >&2; return 2; }
    mkdir -p "$(dirname "$_x11_scale_state")"
    printf '%s\n' "$n" > "$_x11_scale_state"
    X11_SCALE="$n" _x11_apply_scale
    echo "x11 scale set to $n (affects apps launched from now on)"
}

_x11_apply_scale
