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
    ReloadAction Reload,
    string? ConfigRoot = null);

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

            // Os cinco apps abaixo só aceitam config declarativa (JSON/JSONC) — nenhum deles
            // é um script executável, ao contrário do $PROFILE do PowerShell (por isso ele
            // fica de fora da whitelist: um perfil de shell roda código a cada terminal
            // aberto, violaria a regra "manifesto nunca traz scripts").
            ["vscode"] = new(
                "vscode", "VS Code",
                [@"%APPDATA%\Code\User\settings.json"],
                new HashSet<string> { "override" },
                ReloadAction.None,
                ConfigRoot: @"%APPDATA%\Code\User"),

            ["zed"] = new(
                "zed", "Zed",
                [@"%APPDATA%\Zed\settings.json"],
                new HashSet<string> { "override" },
                ReloadAction.None,
                ConfigRoot: @"%APPDATA%\Zed"),

            ["fastfetch"] = new(
                "fastfetch", "Fastfetch",
                [@"%APPDATA%\fastfetch\config.jsonc"],
                new HashSet<string> { "override" },
                ReloadAction.None,
                ConfigRoot: @"%APPDATA%\fastfetch"),

            ["flow_launcher"] = new(
                "flow_launcher", "Flow Launcher",
                [@"%APPDATA%\FlowLauncher\Settings\Settings.json"],
                new HashSet<string> { "override" },
                ReloadAction.None,
                ConfigRoot: @"%APPDATA%\FlowLauncher\Settings"),

            // Sem ConfigPaths: o oh-my-posh não lê um arquivo de nome/local fixo — o tema
            // é referenciado por caminho explícito no $PROFILE (--config <path>), que o
            // OttoRice não edita (é script). Guardamos o tema num diretório próprio do
            // OttoRice; o usuário aponta o profile pra lá manualmente (ver README do tema).
            ["oh_my_posh"] = new(
                "oh_my_posh", "Oh My Posh",
                [],
                new HashSet<string> { "override" },
                ReloadAction.None,
                ConfigRoot: @"%LOCALAPPDATA%\OttoRice\ohmyposh"),
        };

    public static bool IsSupported(string appId) => All.ContainsKey(appId);
}
