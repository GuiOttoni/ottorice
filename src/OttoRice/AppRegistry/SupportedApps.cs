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
}

public sealed record AppDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ConfigPaths,
    IReadOnlySet<string> AllowedActions,
    ReloadAction Reload);

/// <summary>
/// Registry dos apps suportados: é a whitelist de segurança do OttoRice.
/// O manifesto declara só o app alvo; caminhos e reload vêm daqui, nunca do tema.
/// Caminhos com {wt_settings} são resolvidos em runtime (WindowsTerminalLocator).
/// </summary>
public static class SupportedApps
{
    public const string WtSettingsToken = "{wt_settings}";

    public static readonly IReadOnlyDictionary<string, AppDefinition> All =
        new Dictionary<string, AppDefinition>
        {
            ["glazewm"] = new(
                "glazewm", "GlazeWM v3",
                [@"%USERPROFILE%\.glzr\glazewm\config.yaml"],
                new HashSet<string> { "override" },
                ReloadAction.GlazeWm),

            ["zebar"] = new(
                "zebar", "Zebar",
                [@"%USERPROFILE%\.glzr\zebar\"],
                new HashSet<string> { "override" },
                ReloadAction.Zebar),

            ["yasb"] = new(
                "yasb", "YASB Reborn",
                [@"%USERPROFILE%\.config\yasb\config.yaml", @"%USERPROFILE%\.config\yasb\styles.css"],
                new HashSet<string> { "override" },
                ReloadAction.Yasb),

            ["windows_terminal"] = new(
                "windows_terminal", "Windows Terminal",
                [WtSettingsToken],
                new HashSet<string> { "merge_scheme" },
                ReloadAction.None),

            ["wallpaper"] = new(
                "wallpaper", "Papel de parede",
                [],
                new HashSet<string> { "set" },
                ReloadAction.Wallpaper),
        };

    public static bool IsSupported(string appId) => All.ContainsKey(appId);
}
