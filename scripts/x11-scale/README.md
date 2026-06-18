# x11-scale

DPI scaling for GUI apps forwarded from llamabox to chonkers over `ssh -X`/`-Y`.
The X server is WSLg's Xwayland, which reports 96 DPI to forwarded clients
regardless of the actual Windows monitor scaling, so apps render tiny. These
scripts inject the right scale.

## Pieces

- `apply.sh` -> deployed to llamabox `~/.config/x11-scale/apply.sh`, sourced from
  `~/.bashrc` **above the interactive guard**. Reads `X11_SCALE` (from ssh
  `SetEnv`), else `~/.config/x11-scale/scale`, else defaults to 1; exports
  `GDK_SCALE` / `GDK_DPI_SCALE` / `QT_SCALE_FACTOR` / `QT_FONT_DPI` /
  `QT_AUTO_SCREEN_SCALE_FACTOR` / `ELM_SCALE` / `XCURSOR_SIZE` and merges
  `Xft.dpi` via xrdb. `xscale <n>` changes the scale for apps launched later in
  the current shell.
- `win-monitor-dpi.ps1` -> deployed to `%USERPROFILE%\.x11-scale\` on Windows.
  Prints the effective DPI (per-monitor-aware) of the monitor under the cursor.
  Lives on the Windows filesystem because `powershell.exe -File` cannot read a
  `\\wsl$` path.
- `sshx.sh` -> sourced from WSL `~/.bashrc`. `sshx [scale] <ssh args...>`
  auto-detects the Windows scale (rounds DPI/96 to nearest integer) or takes a
  leading integer override, and injects `-o SetEnv=X11_SCALE=<n>`. A one-shot
  remote command is wrapped in a login shell (`bash -lc`) so llamabox's
  `~/.bashrc` (and thus `apply.sh`) is sourced for it; interactive sessions need
  no wrap.

## Install

- llamabox: copy `apply.sh` to `~/.config/x11-scale/`, then run
  `install-llamabox.sh` on llamabox (installs `xorg-xrdb`, inserts the source
  line above the interactive guard, adds the `AcceptEnv X11_SCALE` sshd drop-in,
  reloads sshd).
- chonkers: run `install-wsl.sh` from inside WSL (copies the helper to
  `%USERPROFILE%\.x11-scale\`, installs `sshx.sh` into `~/bin`, sources it from
  `~/.bashrc`).

## Usage

```
sshx -X llamabox steam
sshx -Y llamabox smerge --wait --multiinstance /path/to/repo
sshx -X llamabox                 # interactive; scale applied, then launch apps
sshx 1 -X llamabox xterm         # force scale 1
```

## Known limits

- Fractional scaling: Qt apps, Steam (via `STEAM_FORCE_DESKTOPUI_SCALING`), and
  fonts scale fractionally. GTK *window chrome* only scales in integer steps
  (`GDK_SCALE`), so at e.g. 1.25x text grows but some chrome stays 1x. This is a
  GTK-on-X11 limitation, not a bug.
- Running windows do not rescale; relaunch the app after changing scale.
- **Steam is single-instance**: relaunching only signals the running instance, so
  the scaling env never reaches it. Fully quit first: `ssh llamabox 'steam -shutdown'`
  (wait ~10s), then `sshx -X llamabox steam`.
- One scale per ssh session, read from the monitor under the cursor at launch.
- Default `xterm` uses bitmap fonts and ignores `Xft.dpi`; that is not a scaling
  failure (use `xterm -fa Monospace -fs 10` to see Xft scaling).

## Future (phase B)

MegaSchoen's DisplayManager can detect Windows DPI changes and push `X11_SCALE`
automatically (tray + global hotkey), reusing the same `X11_SCALE` / state-file
contract. Separate spec/plan.
