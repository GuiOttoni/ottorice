<div align="center">
  <img src="src/OttoRice/Assets/ottorice.png" alt="OttoRice logo" width="120" />

  # OttoRice

  **A Windows desktop rice/theme manager** — install complete GlazeWM + YASB + Windows
  Terminal setups (and more) from a JSON manifest, with transactional backup and
  automatic rollback.

  [![CI](https://github.com/GuiOttoni/ottorice/actions/workflows/ci.yml/badge.svg)](https://github.com/GuiOttoni/ottorice/actions/workflows/ci.yml)
  [![Latest Release](https://img.shields.io/github/v/release/GuiOttoni/ottorice?label=release)](https://github.com/GuiOttoni/ottorice/releases/latest)
  [![License: MIT](https://img.shields.io/github/license/GuiOttoni/ottorice)](LICENSE)
  [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
  [![Platform: Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)](https://github.com/GuiOttoni/ottorice/releases/latest)

</div>

---

Think [Omarchy](https://omarchy.org/) or an Arch dotfiles repo, but for Windows: paste
a theme repository's URL, review exactly what will change, and OttoRice installs the
tiling window manager, status bar, terminal color scheme, wallpaper — even editor
themes and Windhawk mods — as one transactional operation you can always undo.

<p align="center">
  <img src="examples/catppuccin/assets/preview.png" alt="Catppuccin Everywhere example theme" width="520" />
  <br />
  <sub>The <code>catppuccin</code> example theme included in this repo</sub>
</p>

## Why

Riced desktops are common on Linux; on Windows, getting there means installing several
independent tools by hand and manually editing their configs, with no easy way back.
OttoRice turns that into: paste a URL → review a preview → install. If anything fails
partway through, everything gets rolled back automatically.

## Features

- **Install from a URL** — GitHub repo, a local folder, or a bare `rice-manifest.json`.
  A preview shows exactly which apps are affected and which WinGet packages will be
  installed before you commit to anything.
- **Transactional pipeline** — dependencies → backup → apply → reload, each step shown
  live (both a written log and a graphical step-by-step status view). Any failure rolls
  back everything already applied.
- **Toggle without uninstalling** — turn the active theme on/off (or just pause GlazeWM's
  tiling) without losing anything.
- **Uninstall cleanly** — restores your previous configuration from backup, and only
  offers to remove WinGet packages when no other installed theme still depends on them.
- **Backup history** — every install creates a session you can restore manually later.

## Supported apps

| App | What OttoRice does |
|---|---|
| [GlazeWM](https://github.com/glzr-io/glazewm) v3 | Overrides `config.yaml` |
| [YASB Reborn](https://github.com/amnweb/yasb) | Overrides `config.yaml` + `styles.css` |
| [Zebar](https://github.com/glzr-io/zebar) | Overrides its config folder |
| Windows Terminal | Merges a color scheme in — never touches your existing profiles/shortcuts |
| Wallpaper | Sets it (and restores the previous one on toggle-off/uninstall) |
| VS Code, Zed, Fastfetch, Flow Launcher, Oh My Posh | Overrides their settings file |
| [Windhawk](https://windhawk.net/) mods (Taskbar/Start Menu/Notification Center Styler) | Installs & configures via `windhawk-cli` (2.0+), one UAC prompt for the whole batch |

A manifest **never** supplies file paths, reload commands, or scripts — those all come
from an internal whitelist (`AppRegistry/SupportedApps.cs`). A theme can only declare
*which* supported app to target and *what* content to apply; where that content lands
and how the app gets reloaded is entirely up to OttoRice, not the theme author.

## Install

Download the latest installer from the
[Releases page](https://github.com/GuiOttoni/ottorice/releases/latest) and run it — no
admin rights required, everything is installed under your user profile.

## Building a theme

A theme is a Git repository (or a local folder) with a `rice-manifest.json` at its root:

```json
{
  "schemaVersion": "1.0",
  "themeId": "my-theme",
  "name": "My Theme",
  "author": "you",
  "dependencies": [{ "wingetId": "glzr-io.glazewm", "minVersion": "3.0.0" }],
  "targets": [
    { "app": "glazewm", "action": "override", "source": "configs/glazewm/config.yaml" },
    { "app": "wallpaper", "action": "set", "source": "assets/wallpaper.png" }
  ]
}
```

See [`examples/`](examples/) for three complete reference themes of increasing scope —
[`blackturq`](examples/blackturq) (minimal), [`voidhaze`](examples/voidhaze), and
[`catppuccin`](examples/catppuccin) (all nine supported apps, Windhawk mods included) —
each with its own README explaining the format and any gotchas.

## Development

```powershell
dotnet run --project src/OttoRice   # run the app
dotnet test                          # run the test suite
```

- **Stack:** .NET 10, Avalonia UI 12, CommunityToolkit.Mvvm, Serilog. Vertical-slice
  architecture (`Features/`), with `AppRegistry/` holding the app whitelist and
  `Common/` holding shared infrastructure (process execution, WinGet, wallpaper, atomic
  file writes).
- Process termination is always restricted to a whitelist and always by specific PID —
  never a mass kill by process name.

## License

[MIT](LICENSE)
