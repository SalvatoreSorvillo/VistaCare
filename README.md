<div align="center">

# VistaCare

### Make your screens darker than Windows lets you.

A tiny single-`.exe` Windows tray app that dims **all** your monitors **below their hardware
minimum** — without the "bright cursor / bright taskbar" bugs that overlay-based dimmers have.

[![Download](https://img.shields.io/badge/Download-VistaCare.exe-2ea44f?style=for-the-badge)](https://github.com/SalvatoreSorvillo/VistaCare/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=for-the-badge)](LICENSE)
![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?style=for-the-badge)

<!-- TODO: add a short screen recording at docs/demo.gif and uncomment the line below -->
<!-- ![VistaCare demo](docs/demo.gif) -->

</div>

---

Working at night and your monitor's lowest brightness is **still** too bright? VistaCare lowers the
actual luminance of every display in software — as dark as you want. Instead of laying a black
overlay over the screen, it scales the GPU's **gamma ramp**, which is applied at the very last
display stage (*after* the cursor and taskbar are drawn). So **everything** dims evenly: the mouse
cursor, the taskbar, the Start menu, even fullscreen video. One ~41 KB executable. No installer, no
admin, no dependencies.

## Why not just use f.lux or Twinkle Tray?

Those change *color temperature* or drive your monitor's *hardware* brightness (DDC/CI) — which
still bottoms out at the panel's minimum and needs monitor support. VistaCare lowers real luminance
in software, on any display.

| | **VistaCare** | f.lux | Twinkle Tray / Monitorian | Overlay dimmers (Dimmer, PangoBright) |
|---|:--:|:--:|:--:|:--:|
| Dims **below** the hardware minimum | ✅ | ❌ | ❌ | ✅ |
| Cursor + taskbar dim too | ✅ | ✅ | ✅ | ❌ (bright cursor) |
| Works without monitor DDC support | ✅ | ✅ | ❌ | ✅ |
| Single exe, no install | ✅ | ❌ | ❌ | ~ |

## Install

**Option 1 — download (recommended).** Grab `VistaCare.exe` from the
[**latest release**](https://github.com/SalvatoreSorvillo/VistaCare/releases/latest) and
double-click it. It lives in your system tray.

> Because the app isn't code-signed, Windows may show *"Windows protected your PC."* Click
> **More info → Run anyway**. The exe is a single ~41 KB file and its SHA-256 is published with
> each release so you can verify it.

**Option 2 — winget**
```powershell
winget install SalvatoreSorvillo.VistaCare
```

**Option 3 — build it yourself.** See [Build from source](#build-from-source) below.

## Controls

<!-- TODO: add screenshots at docs/slider.png and docs/menu.png -->

- **Left-click** the tray icon → brightness slider (opens next to the cursor; drag down to dim).
- **Right-click** the tray icon → presets (100%…20%), **Pause**, **Start automatically after ▸**
  (pick a time, or *Off*), **Exit**.
- **Hotkeys** (work anywhere):
  - `Ctrl+Alt+PageDown` → dimmer
  - `Ctrl+Alt+PageUp` → brighter
  - `Ctrl+Alt+Home` → reset to 100%

**Pause** returns the screen to full brightness without forgetting your level — uncheck it (or
change the level any other way) to resume. Your last level is remembered, and exiting restores full
brightness.

## Auto-start at a time you choose

Under **Start automatically after**, pick an hour (noon–11pm) or turn it *Off*. VistaCare registers
a per-user **Scheduled Task** (daily at that hour **and** at logon) — far more reliable than a
`Run`-key entry, which can fire before the shell/GPU/drive are ready. It only auto-opens from your
chosen hour onward (a manual launch always opens), and a slot missed while the PC was off/asleep
runs at the next opportunity. No admin required.

## Notes & limits (inherent to gamma dimming, not bugs)

- **HDR:** Windows ignores gamma changes for monitors in HDR mode — turn HDR off to dim them.
- **Night Light / f.lux:** they drive the same single gamma table, so running them together makes
  the colors fight. Use one or the other.
- **Dimming floor:** Windows clamps how dark gamma can go. To go darker, set this DWORD and reboot:
  `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiICMGammaRange = 256`
- If the app is ever force-killed while dim (e.g. Task Manager → End task), just relaunch it and
  press `Ctrl+Alt+Home`.

## Build from source

No SDK or Visual Studio needed — it uses the C# compiler built into Windows.

1. Edit `VistaCare.cs`.
2. Make sure no `VistaCare.exe` is running (the running file is locked).
3. Run **`build.bat`** → produces a fresh `VistaCare.exe`.

`build.bat` embeds `VistaCare.ico` into the exe, so keep `VistaCare.cs` + `VistaCare.ico` +
`build.bat` together. To change the offered auto-start hours, edit `FirstHour`/`LastHour` in
`VistaCare.cs`.

## How it works (the short version)

A standard overlay dimmer paints a semi-transparent black window over your desktop — but the cursor
and some surfaces render *above* it, so they stay bright. VistaCare instead rewrites the GPU's gamma
lookup table for every attached monitor, which the display pipeline applies to the final composited
image. The result is uniform and overlay-free. It re-asserts the dim after Windows resets the gamma
table (sleep/resume, unlock, resolution change) and always restores full brightness on exit.

## License

[MIT](LICENSE) © 2026 Salvatore Sorvillo
