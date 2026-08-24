using System.Collections.Generic;

namespace OttoRice.AppRegistry;

/// <summary>Ações de reload permitidas — whitelist; o manifesto nunca fornece comandos.</summary>
public enum ReloadAction
{
    None,
    GlazeWm,
    Yasb,
    Zebar,
    Wallpaper,
    TranslucentTb,
}

public sealed record AppDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ConfigPaths,
    IReadOnlySet<string> AllowedActions,
    ReloadAction Reload,
    string? ConfigRoot = null,
    /// <summary>
    /// Quando definido, o TargetPlanner resolve o diretório de config dinamicamente pela
    /// pasta do executável real (seguindo symlink), em vez de usar <see cref="ConfigRoot"/>
    /// estático. Necessário para apps instalados via winget portable, cujo caminho real
    /// só é conhecido em runtime (ex.: TranslucentTB).
    /// </summary>
    string? ExecutableName = null);

/// <summary>
/// Registry dos apps suportados: é a whitelist de segurança do OttoRice.
/// O manifesto declara só o app alvo; caminhos e reload vêm daqui, nunca do tema.
/// ConfigPaths: arquivos individuais conhecidos (match por nome no planner).
/// ConfigRoot: diretório-base para sources de tema que são pastas inteiras.
/// </summary>
public static class SupportedApps
{
    public static readonly IReadOnlyDictionary<string, AppDefinition> All =
        new Dictionary<string, AppDefinition>
        {
            ["glazewm"] = new(
                "glazewm", "GlazeWM v3",
                [@"%USERPROFILE%\.glzr\glazewm\config.yaml"],
                new HashSet<string> { "override" },
                ReloadAction.GlazeWm,
                ConfigRoot: @"%USERPROFILE%\.glzr\glazewm"),

            ["zebar"] = new(
                "zebar", "Zebar",
                [],
                new HashSet<string> { "override" },
                ReloadAction.Zebar,
                ConfigRoot: @"%USERPROFILE%\.glzr\zebar"),

            ["yasb"] = new(
                "yasb", "YASB Reborn",
                [@"%USERPROFILE%\.config\yasb\config.yaml", @"%USERPROFILE%\.config\yasb\styles.css"],
                new HashSet<string> { "override" },
                ReloadAction.Yasb,
                ConfigRoot: @"%USERPROFILE%\.config\yasb"),

            ["windows_terminal"] = new(
                "windows_terminal", "Windows Terminal",
                [],
                new HashSet<string> { "merge_scheme" },
                ReloadAction.None),

            ["wallpaper"] = new(
                "wallpaper", "Papel de parede",
                [],
                new HashSet<string> { "set" },
                ReloadAction.Wallpaper),

            // Restiliza a taskbar nativa (não a esconde) — accent/cor via settings.json.
            // Instalado via winget portable: o exe no PATH é um symlink, a config real
            // fica ao lado do exe de verdade (ver TargetPlanner.ResolveConfigRootFromExecutable).
            ["translucenttb"] = new(
                "translucenttb", "TranslucentTB",
                ["settings.json"],
                new HashSet<string> { "override" },
                ReloadAction.TranslucentTb,
                ExecutableName: "TranslucentTB"),
        };

    public static bool IsSupported(string appId) => All.ContainsKey(appId);
}
