---
name: generate-ottorice-theme
description: "Generate a brand-new, complete OttoRice theme (rice-manifest.json + per-app configs + wallpaper/preview + README) from a prompt like 'make me a theme inspired by X' or 'make me a theme with palette Y'. Use whenever the user asks for a new OttoRice rice/theme, a Catppuccin/Gruvbox/Nord/Tokyo-Night-style desktop theme for OttoRice, or to adapt a community GlazeWM/YASB/Komorebi dotfiles repo into an OttoRice manifest. Triggers on: make me a theme, new rice, OttoRice theme, rice-manifest.json, theme inspired by, palette theme."
---

# generate-ottorice-theme

Generates a complete, whitelist-compliant OttoRice theme end-to-end: gather intent →
write manifest + configs → generate art → write README → validate (syntax +
real-device deploy when possible). OttoRice (`F:/Projetos/ottorice`) is a Windows
desktop app that installs "rices" from a `rice-manifest.json`; **the manifest can only
ever declare *which* whitelisted app to target and *what* content to apply** — it never
supplies file paths, reload commands, or scripts. Read
`src/OttoRice/AppRegistry/SupportedApps.cs` yourself before starting if this skill's
summary of it (step 2 below) is more than a few weeks stale.

Reference: `F:/Projetos/docs/docs/projetos/desktop-hardware/ottorice.md` section 10
documents real bugs found dogfooding this exact task on a real machine — read it if
anything below seems surprising, it's the primary source for the gotchas here.

## 1. Gather intent

Ask (or infer from the prompt) before generating anything:

- **Palette source** — a named palette (Catppuccin Mocha/Latte, Gruvbox, Nord, Tokyo
  Night, Rosé Pine, etc.), a supplied image to extract colors from, or a vibe/inspiration
  ("cyberpunk", "inspired by <some dotfiles repo>"). If it's a known palette, use its
  *official* hex values (e.g. catppuccin.com/palette) — don't invent hex codes.
- **Which apps to cover.** Default to a reasonable subset unless told otherwise:
  `glazewm` + `yasb` + `windows_terminal` + `wallpaper` is the minimum for something
  that reads as "a rice". Ask before adding `vscode`/`zed`/`fastfetch`/`flow_launcher`/
  `oh_my_posh`/`zebar` — the full 9-app catppuccin example is the ceiling, not the
  default.
- **Windhawk mods?** Only if explicitly requested — they need a manual Windhawk 2.0+
  alpha install by the user first (not installed by OttoRice) and add real complexity.
  Say so up front if the user asks for taskbar/start-menu restyling.
- **Adapting a specific community theme/repo?** If the user names a real dotfiles repo
  (e.g. an `amnweb/yasb-themes` entry, a windots-style repo), fetch and read its actual
  config files (WebFetch/browse) rather than improvising — see step 2's YASB and GlazeWM
  notes for the translation rules you'll need to apply.

## 2. Directory layout and the whitelist

Create `examples/<theme-id>/` (kebab-case `theme-id`) mirroring the existing examples:

```
examples/<theme-id>/
  rice-manifest.json
  configs/
    glazewm/config.yaml
    yasb/config.yaml
    yasb/styles.css
    wt-scheme.json
    vscode/settings.json          (if targeted)
    zed/settings.json             (if targeted)
    fastfetch/config.jsonc        (if targeted)
    flow_launcher/settings.json   (if targeted)
    ohmyposh/<name>.omp.json      (if targeted)
    windhawk/<mod>.yaml           (if targeted)
  assets/
    wallpaper.png
    preview.png
  README.md
```

**Manifest shape** (`schemaVersion` always `"1.0"`, `themeId` kebab-case
`^[a-z0-9]+(-[a-z0-9]+)*$`, `name`/`author` required, `targets` non-empty):

```json
{
  "schemaVersion": "1.0",
  "themeId": "my-theme",
  "name": "My Theme",
  "author": "you",
  "preview": "assets/preview.png",
  "dependencies": [{ "wingetId": "glzr-io.glazewm", "minVersion": "3.0.0" }],
  "targets": [
    { "app": "glazewm", "action": "override", "source": "configs/glazewm/config.yaml" },
    { "app": "wallpaper", "action": "set", "source": "assets/wallpaper.png" }
  ]
}
```

`source` is always a path relative to the theme directory, never absolute, never
containing `..`. Every `target.app` + `target.action` pair **must** come from this
whitelist (`SupportedApps.cs` — re-check it if this list is stale):

| `app` | allowed `action`(s) | notes |
|---|---|---|
| `glazewm` | `override` | file or whole `configs/glazewm/` folder |
| `yasb` | `override` | `config.yaml` + `styles.css`, file or folder |
| `zebar` | `override` | only if you're doing Zebar instead of/alongside YASB |
| `windows_terminal` | `merge_scheme` | merges a color scheme in; never touches profiles/keybindings. `setAsDefault: true` optional |
| `wallpaper` | `set` | source is the image itself |
| `vscode` | `override` | `%APPDATA%\Code\User\settings.json` |
| `zed` | `override` | `%APPDATA%\Zed\settings.json` |
| `fastfetch` | `override` | `%APPDATA%\fastfetch\config.jsonc` |
| `flow_launcher` | `override` | `%APPDATA%\FlowLauncher\Settings\Settings.json` — **only** touch stable fields (hotkey, language); never invent a theme name (installed theme names vary per install/plugins) |
| `oh_my_posh` | `override` | no fixed path — theme lands in `%LOCALAPPDATA%\OttoRice\ohmyposh\`; document in the README that the user must add a `$PROFILE` line pointing `--config` at it (OttoRice never edits `$PROFILE` — it's a script) |
| `windows-11-taskbar-styler`, `windows-11-start-menu-styler`, `windows-11-notification-center-styler` | `configure_mod` | Windhawk mods, see below |

Never target an app not in this table, never invent an `action` for an app, never emit
a `targetPath`/`reloadCommand`/script field — the manifest schema has no such fields and
`ManifestValidator` will reject anything else anyway.

**`configure_mod` (Windhawk).** Two ways to set values, combinable (`settings` wins on
key conflicts): an inline `"settings": {"key": "value"}` dict of flat string pairs, and/or
`"source": "configs/windhawk/<mod>.yaml"` pointing at a YAML in the **same nested/textual
format the Windhawk UI itself shows** for that mod's settings (e.g. `theme: 'FrostyGlass'`,
`controlStyles: [{ target: '', styles: [''] }]`) — see
`examples/catppuccin/configs/windhawk/taskbar-styler.yaml` for a real one. Never write a
raw script or `.cmd` for this — settings only. Pick a real theme name that exists in that
mod (check `examples/catppuccin/README.md`'s Windhawk section for known-good ones like
`FrostyGlass`/`Down Aero`/`RosePine`/`DockLike`/`Squircle`); don't invent one. If no exact
palette match exists among the mod's built-in themes, say so in the README and pick the
closest coherent family rather than forcing a mismatch.

## 3. Writing the per-app configs — don't guess schemas

- **GlazeWM v3 (`config.yaml`)**: base structure on
  `examples/catppuccin/configs/glazewm/config.yaml` (`general`, `gaps`, `window_effects`,
  `window_behavior`, `workspaces`, `keybindings`). Recolor `window_effects.focused_window
  .border.color` / `other_windows.border.color` to the theme's accent/muted colors. The
  **old `window_rules`/`match_process_name` syntax does not exist in GlazeWM v3.** If you
  ever need to exclude a window from tiling (rare — YASB doesn't need this, see next
  bullet), the real v3 syntax is nested `match:` conditions, e.g.
  `match: [{ window_process: { equals: 'zebar' } }]` — this is only relevant for Zebar.
- **YASB must never be tiled by GlazeWM.** Set
  `bars.<bar-name>.window_flags.windows_app_bar: true` in the YASB `config.yaml`. This
  registers the bar as a Windows AppBar (same mechanism as the native taskbar) so it
  reserves its own screen strip — without it GlazeWM tiles the YASB window like a normal
  app and stretches it across the whole monitor. Do **not** try to solve this with a
  GlazeWM window rule.
- **YASB CSS class names — the single most common bug.** Every widget in the `yasb.*`
  namespace gets a `-widget` suffix on its real CSS class
  (`cpu-widget`, `memory-widget`, `wifi-widget`, `volume-widget`, `battery-widget`,
  `power-menu-widget`, `active-window-widget`, `clock-widget`, `taskbar-widget`,
  `open-meteo-widget`, ...). Widgets in the `glazewm.*` namespace do **not** get the
  suffix (`glazewm-workspaces`, `glazewm-tiling-direction`). Bar-level selectors
  (`.yasb-bar`, `.dark`, `.widget .icon`, `.widget .label`) are correct as-is. Getting the
  suffix wrong means the CSS silently never matches anything — this exact bug shipped in
  the `blackturq`/`voidhaze` example themes and was only caught by live-deploying and
  screenshotting. Check every custom class selector you write against this rule before
  calling the CSS done; when unsure of a widget's real class name, read the widget's
  Python source (`class_name=` in
  `https://github.com/amnweb/yasb/tree/main/src/core/widgets`) rather than guessing.
- **Don't invent YASB widget option keys.** For each widget's config schema, cross-check
  against `https://github.com/amnweb/yasb/tree/main/src/core/validation/widgets`
  (the actual Pydantic schema, including renamed/deprecated keys) — not memory, not the
  widget's README alone. `examples/catppuccin/configs/yasb/config.yaml` is a solid
  reference for common widgets (`clock`, `cpu`, `memory`, `volume`, `power_menu`,
  `systray`, `taskbar`, `home`, `quick_launch`, `open_meteo`, `glazewm.workspaces`).
- **Weather widget: never embed a real API key.** Use `yasb.open_meteo.OpenMeteoWidget`
  (`type: 'yasb.open_meteo.OpenMeteoWidget'`), which needs no API key — not the older
  `weather` widget, which does.
- **Adapting a community YASB theme built for Komorebi** (common on
  `https://yasb.dev/themes`, index at `themes.json` mapping name → UUID folder, source
  repo usually `amnweb/yasb-themes`): translate `komorebi_workspaces` →
  `glazewm_workspaces` (`glazewm.workspaces.GlazewmWorkspacesWidget`),
  `komorebi_active_layout` → `glazewm_tiling_direction`
  (`glazewm.tiling_direction.GlazewmTilingDirectionWidget`), and **delete** any top-level
  `komorebi:` start/stop/reload block entirely — OttoRice's own `AppReloader` owns
  GlazeWM/YASB reload, a theme's YASB config must never try to manage that itself. Also
  strip anything hardcoding the original author's personal paths (`C:\Users\<name>\...`)
  or personal API keys.
- **Windows Terminal scheme** (`configs/wt-scheme.json`): a single color-scheme object
  (`name`, `background`, `foreground`, `black`..`white`/`brightBlack`..`brightWhite`,
  `cursorColor`, `selectionBackground`) — see any example's `wt-scheme.json`. This gets
  merged in, never overwrites the user's profiles.
- **VS Code / Zed**: set `workbench.colorTheme`/`theme` + a font family the theme
  plausibly ships with, matching `examples/catppuccin/configs/{vscode,zed}/settings.json`.
  Note in the README that the color theme extension itself isn't installed by OttoRice
  (not a WinGet package) — the user installs it from each editor's marketplace.
- **Fastfetch**: `configs/fastfetch/config.jsonc`, modules list + color config, see the
  catppuccin example.
- **Oh My Posh**: a `.omp.json` prompt theme, no fixed install path (see table above) —
  document the manual `$PROFILE` step in the README.

## 4. Wallpaper + preview image

If the user supplied a real reference image, derive the wallpaper/preview from it
(resize/crop as needed) instead of generating art. Otherwise, generate a simple gradient
as the acceptable fallback (same approach the shipped examples use — a diagonal gradient
across 2-4 palette colors plus an optional soft glow blob). Use the bundled helper:

```
python "F:/Projetos/ottorice/.claude/skills/generate-ottorice-theme/scripts/gen_gradient.py" \
  --colors "#1e1e2e,#cba6f7,#89b4fa" \
  --wallpaper examples/<theme-id>/assets/wallpaper.png \
  --preview examples/<theme-id>/assets/preview.png \
  --wallpaper-size 3840x2160 --preview-size 1040x585
```

(Requires Pillow — `pip install pillow` if missing.) Pick 2-4 colors from the theme's own
palette (typically: base/background color first, accent color(s) last) so the gradient
reads as "this theme," not generic.

## 5. Write the README

One `README.md` per theme (see the three existing ones as models, `catppuccin`'s is the
most complete). Cover: what it is / what it's inspired by and any attribution (community
theme/repo names and authors, e.g. "adapted from windots (ashish0kumar)" or "YASB visual
based on <theme> by <author>, amnweb/yasb-themes"); a content listing; which apps are
covered and why any whitelisted-but-obvious app was deliberately left out (PowerShell
profile is *always* out of scope — it's a script, explain briefly if relevant); any manual
steps the user still has to do (Windhawk install, editor theme extensions, `$PROFILE` line
for Oh My Posh, Nerd Font requirement if you used glyph icons vs. Segoe Fluent Icons if you
didn't); and an honest note on what was/wasn't verified (see step 6).

## 6. Verify — don't skip this

**Always**, regardless of environment:
- Parse every JSON file (`rice-manifest.json` and all `configs/**/*.json`/`.jsonc`) and
  every YAML file (`configs/**/*.yaml`) to confirm they're syntactically valid. Use a
  real parser, not eyeballing — e.g. `python -c "import json,sys; json.load(open(sys.argv[1]))"`
  per JSON file, and `python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))"` per
  YAML file (PyYAML; install if missing).
- Re-read every YASB CSS selector you wrote against the `-widget` suffix rule in step 3 —
  grep for `.yasb\.` namespace widget base names in `config.yaml` and confirm each has a
  matching `<name>-widget` selector (not bare `<name>`) in `styles.css`, and confirm
  `glazewm.*` widgets do *not* have the suffix.
- Confirm every `target.app`/`target.action` pair in the manifest is actually in the
  whitelist table from step 2, and every `source` path exists on disk relative to the
  theme root.

**When a real Windows machine with OttoRice + GlazeWM + YASB installed is available**
(as it was during the session that produced the existing example themes) — don't declare
the theme "done"/"working" without also doing this:
1. Deploy the YASB files for real: copy `configs/yasb/config.yaml` and `styles.css` to
   `%USERPROFILE%\.config\yasb\`.
2. Reload: `yasbc reload`.
3. Take a screenshot and actually look at it — bar present, not tiled/stretched, colors
   and layout as intended.
4. Check `%USERPROFILE%\.config\yasb\yasb.log` for errors/warnings after the reload.
5. If GlazeWM config also changed, reload it too and confirm via screenshot that the WM
   didn't break (YASB bar not tiled, borders/gaps as configured).

If no such machine is available in this environment, say so explicitly in the final
report/README rather than claiming verification that didn't happen — match the honesty
of the existing examples' README footers (e.g. "configs validated for manifest format
and install pipeline, but not run against the real apps simultaneously").
