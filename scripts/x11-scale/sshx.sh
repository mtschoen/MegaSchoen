# shellcheck shell=bash
# sshx - ssh wrapper that injects X11_SCALE so forwarded GUI apps get DPI scaling.
# Source from WSL ~/.bashrc. Usage:
#   sshx -X llamabox steam                              # auto-detect Windows scale
#   sshx -Y llamabox smerge --wait --multiinstance /repo
#   sshx 1 -X llamabox xterm                            # leading integer = explicit override
#   sshx -X llamabox                                    # interactive session, scale applied
#
# Scale travels as a single SetEnv X11_SCALE; the llamabox applier (sourced from
# ~/.bashrc above the interactive guard) derives the toolkit vars and Xft.dpi.
# A one-shot remote command is wrapped in a login shell (bash -lc) so ~/.bashrc
# is sourced for it too (a plain non-login one-shot would not source it).
#
# The ssh binary is injectable via $SSHX_SSH for testing.
sshx() {
    local scale=""
    case "${1:-}" in
        ''|*[!0-9.]*|*.*.*) : ;;     # first arg not a bare number -> auto-detect
        *) scale="$1"; shift ;;      # leading number (e.g. 1.5) -> explicit override
    esac

    if [ -z "$scale" ]; then
        # The DPI helper must live on the Windows filesystem: powershell.exe
        # -File rejects a \\wsl$ path, so we keep win-monitor-dpi.ps1 under
        # %USERPROFILE%\.x11-scale and pass a real C:\ path. Resolve the
        # Windows home once per shell (cached in _SSHX_HELPER_W).
        local dpi
        if [ -z "${_SSHX_HELPER_W:-}" ]; then
            local wh
            # shellcheck disable=SC2016  # $env:USERPROFILE is PowerShell, must not expand in bash
            wh="$(powershell.exe -NoProfile -Command '[Console]::Write($env:USERPROFILE)' 2>/dev/null | tr -d '\r')"
            _SSHX_HELPER_W="${wh}\\.x11-scale\\win-monitor-dpi.ps1"
        fi
        dpi="$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$_SSHX_HELPER_W" 2>/dev/null | tr -d '\r')"
        case "$dpi" in
            ''|*[!0-9]*) echo "sshx: could not read Windows DPI, using scale 1" >&2; scale=1 ;;
            *) scale="$(awk -v d="$dpi" 'BEGIN{printf "%.4f", d/96.0}')" ;;  # fractional factor
        esac
    fi
    awk "BEGIN{exit !($scale+0 >= 1)}" 2>/dev/null || scale=1

    # Split args into ssh-args (up to and including the host) and remote command.
    # valopts: single-letter ssh options that consume the following token.
    local valopts="BbcDEeFIiJLlmOopQRSWw"
    local -a sshargs=()
    while [ "$#" -gt 0 ]; do
        local a="$1"
        case "$a" in
            --) sshargs+=("$a"); shift
                if [ "$#" -gt 0 ]; then sshargs+=("$1"); shift; fi
                break ;;
            -?) sshargs+=("$a"); shift
                local opt="${a#-}"
                case "$valopts" in
                    *"$opt"*) if [ "$#" -gt 0 ]; then sshargs+=("$1"); shift; fi ;;
                esac ;;
            -*) sshargs+=("$a"); shift ;;   # bundle or attached value, no separate token
            *)  sshargs+=("$a"); shift; break ;;   # host
        esac
    done

    local ssh_bin="${SSHX_SSH:-ssh}"
    if [ "$#" -eq 0 ]; then
        "$ssh_bin" -o "SetEnv=X11_SCALE=$scale" "${sshargs[@]}"
    else
        local inner; printf -v inner '%q ' "$@"
        local esc=${inner//\'/\'\\\'\'}
        "$ssh_bin" -o "SetEnv=X11_SCALE=$scale" "${sshargs[@]}" "bash -lc '$esc'"
    fi
}
