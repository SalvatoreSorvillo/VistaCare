# VistaCare 1.0.0

**Make your screens darker than Windows lets you.**

VistaCare dims every monitor *below* its hardware minimum by scaling the GPU gamma ramp - so the
cursor, taskbar, Start menu and fullscreen apps all dim uniformly, with no overlay and no
bright-cursor bug. One tiny executable, no installer, no admin, no dependencies.

## Download
Download **`VistaCare.exe`** below and double-click it. It lives in your system tray.

> **SmartScreen note:** because the app isn't code-signed, Windows may show
> *"Windows protected your PC."* Click **More info → Run anyway**. (The exe is a 41 KB
> single file; the SHA-256 is listed below so you can verify it.)

## Highlights
- **Dims below the hardware floor** - go darker than your monitor's lowest brightness setting.
- **Uniform dimming** - gamma-based, so the cursor and taskbar dim too (unlike overlay dimmers).
- **All monitors / multiple GPUs**, skips mirrored/phantom displays.
- **Tray slider, presets, and global hotkeys** - `Ctrl+Alt+PageUp/PageDown/Home`.
- **Pause** - jump back to full brightness without losing your level.
- **Configurable evening auto-start** - pick a time (noon–11pm) from the menu, or off.
- **Robust** - re-applies after sleep/resume, unlock, and resolution changes; restores full
  brightness on exit or crash; single-instance.

## Requirements
- Windows 10 or 11 (uses the built-in .NET Framework 4.x - nothing to install).
- Not for HDR monitors (Windows ignores gamma in HDR mode) and don't run alongside Night Light /
  f.lux (they share the same gamma table).

## Verify your download
```
SHA-256 (VistaCare.exe): D7FD2D6488251AABBD0CB3B31727143698A3D87BE2CCA3D8CAD16F56FF85CD22
```
PowerShell: `Get-FileHash .\VistaCare.exe -Algorithm SHA256`

> The hash above is for the build shipped with this release. If you rebuild from source, your
> exe will have a different hash - recompute it before publishing.

**Full changelog:** first public release.
