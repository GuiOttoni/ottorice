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
    /// <summary>Sem comando de "reload a quente" real (reload-configuration do komorebic é
    /// só para os formatos legados .ahk/.ps1) — a ação sempre para (se rodando) e reinicia.</summary>
    Komorebi,
    /// <summary>App-launcher persistente (tipo Spotlight/Alfred/PowerToys Run) — não tem CLI de
    /// reload, só precisa estar rodando: inicia se não estiver, não faz nada se já estiver.</summary>
    FlowLauncher,
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

            // Komorebi (fase 2, RF-10): config em duas partes (verificado ago/2026 contra
            // LGUG2Z/komorebi docs/installation.md e komorebic/src/main.rs — não é o
            // "komorebi.json + whkd.yaml" chutado na v1 desta doc): `komorebi.json` direto em
            // %USERPROFILE% e `whkdrc` (sintaxe própria estilo skhd/sxhkd, não YAML apesar do
            // nome comum "whkd.yaml" em alguns tutoriais) em %USERPROFILE%\.config\whkdrc.
            // Sem ConfigRoot: os dois arquivos não compartilham diretório-base, então o tema
            // sempre declara os dois arquivos individualmente (nunca um override de pasta).
            ["komorebi"] = new(
                "komorebi", "Komorebi",
                [@"%USERPROFILE%\komorebi.json", @"%USERPROFILE%\.config\whkdrc"],
                new HashSet<string> { "override" },
                ReloadAction.Komorebi),

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

            // Diferente dos outros quatro apps deste bloco, o Flow Launcher é um launcher
            // persistente (fica rodando em background, tipo Spotlight/Alfred/PowerToys Run) —
            // depois de instalado ele precisa estar de fato em execução, não só ter a config
            // escrita. ReloadAction.FlowLauncher cobre isso (ver AppReloader): sem CLI de
            // reload, só sobe o processo se ele não estiver rodando.
            ["flow_launcher"] = new(
                "flow_launcher", "Flow Launcher",
                [@"%APPDATA%\FlowLauncher\Settings\Settings.json"],
                new HashSet<string> { "override" },
                ReloadAction.FlowLauncher,
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

            // Mods do Windhawk (ação "configure_mod", ver ConfigureWindhawkModsStep): não são
            // config de arquivo — Id aqui É o id real do mod no repositório do Windhawk,
            // repassado só pro `windhawk-cli`, nunca lido do manifesto diretamente (o
            // manifesto só pode referenciar estas três chaves da whitelist). Windhawk em si
            // não é instalado pelo OttoRice (só a build 2.0 alpha tem o `windhawk-cli`, ainda
            // não está no WinGet) — é pré-requisito manual, ver README dos temas.
            ["windows-11-taskbar-styler"] = new(
                "windows-11-taskbar-styler", "Windows 11 Taskbar Styler (Windhawk)",
                [], new HashSet<string> { "configure_mod" }, ReloadAction.None),

            ["windows-11-start-menu-styler"] = new(
                "windows-11-start-menu-styler", "Windows 11 Start Menu Styler (Windhawk)",
                [], new HashSet<string> { "configure_mod" }, ReloadAction.None),

            ["windows-11-notification-center-styler"] = new(
                "windows-11-notification-center-styler", "Windows 11 Notification Center Styler (Windhawk)",
                [], new HashSet<string> { "configure_mod" }, ReloadAction.None),
        };

    public static bool IsSupported(string appId) => All.ContainsKey(appId);
}
