using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Reloaders;

/// <summary>
/// Reload dos apps da whitelist. Comandos fixos — nada vem do manifesto.
/// Se o app não está rodando (o reload falha), ele é iniciado.
/// Os executáveis são resolvidos por caminho absoluto: GlazeWM/YASB instalam em
/// Program Files sem entrar no PATH, e o PATH do processo é anterior à instalação.
/// </summary>
public sealed class AppReloader(
    IProcessRunner runner, IExecutableResolver resolver, ILogger<AppReloader>? logger = null) : IAppReloader
{
    /// <summary>Nome do processo (sem extensão) esperado rodando após cada ação — usado tanto
    /// aqui (FlowLauncher) quanto pelo validador pós-reload em ReloadStep.</summary>
    private static readonly IReadOnlyDictionary<ReloadAction, string> ProcessNames =
        new Dictionary<ReloadAction, string>
        {
            [ReloadAction.GlazeWm] = "glazewm",
            [ReloadAction.Yasb] = "yasb",
            [ReloadAction.Zebar] = "zebar",
            [ReloadAction.FlowLauncher] = "Flow.Launcher",
            [ReloadAction.Komorebi] = "komorebi",
        };

    public string? ExpectedProcessName(ReloadAction action) => ProcessNames.GetValueOrDefault(action);

    public async Task<Result> ReloadAsync(ReloadAction action, CancellationToken ct = default)
    {
        return action switch
        {
            ReloadAction.GlazeWm => await ReloadOrStartAsync(
                "glazewm", "command wm-reload-config", "start", ct),
            ReloadAction.Yasb => await ReloadOrStartAsync(
                "yasbc", "reload", "start --silent", ct),
            // Zebar é iniciado/gerenciado pelos startup_commands do próprio GlazeWM.
            ReloadAction.Zebar => Result.Ok(),
            // Flow Launcher não tem CLI de reload — a config é lida ao iniciar (ou já ao vivo).
            // Só precisa estar rodando: sobe se não estiver, não faz nada se já estiver.
            ReloadAction.FlowLauncher => StartIfNotRunning("Flow.Launcher"),
            ReloadAction.Komorebi => await ReloadKomorebiAsync(ct),
            ReloadAction.None or ReloadAction.Wallpaper => Result.Ok(),
            _ => Result.Fail($"Ação de reload desconhecida: {action}"),
        };
    }

    /// <summary>
    /// Komorebi não tem um comando de reload "a quente" para o komorebi.json atual — o
    /// `reload-configuration` do komorebic é só para os formatos legados .ahk/.ps1
    /// (confirmado no código-fonte do komorebic, ago/2026). O padrão real (documentado em
    /// docs/installation.md do próprio projeto) é parar e iniciar de novo: `komorebic stop`
    /// já restaura as janelas ocultas antes de sair, então é seguro repetir a cada reload —
    /// mais limpo que o `wm-exit` do GlazeWM, que não restaura posição/tamanho.
    /// </summary>
    private async Task<Result> ReloadKomorebiAsync(CancellationToken ct)
    {
        var exePath = resolver.Resolve("komorebic");
        if (exePath is null)
        {
            logger?.LogWarning("'komorebic' não encontrado (nem no PATH nem nos locais de instalação conhecidos).");
            return Result.Fail("'komorebic' não encontrado (nem no PATH nem nos locais de instalação conhecidos).");
        }

        try
        {
            if (runner.FindProcessIds("komorebi").Count > 0)
            {
                var stop = await runner.RunAsync(exePath, "stop --whkd", ct);
                logger?.LogInformation(
                    "'{ExePath} stop --whkd' saiu com código {ExitCode} antes do reinício.", exePath, stop.ExitCode);
            }

            runner.StartDetached(exePath, "start --whkd");
            logger?.LogInformation("Komorebi reiniciado (stop+start) via '{ExePath}'.", exePath);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger?.LogError(ex, "Falha ao executar '{ExePath}'.", exePath);
            return Result.Fail($"Falha ao executar '{exePath}': {ex.Message}");
        }
    }

    private Result StartIfNotRunning(string exeName)
    {
        if (runner.FindProcessIds(exeName).Count > 0)
        {
            logger?.LogInformation("'{ExeName}' já está rodando — nada a fazer.", exeName);
            return Result.Ok();
        }

        var exePath = resolver.Resolve(exeName);
        if (exePath is null)
        {
            logger?.LogWarning("'{ExeName}' não encontrado (nem no PATH nem nos locais de instalação conhecidos).", exeName);
            return Result.Fail($"'{exeName}' não encontrado (nem no PATH nem nos locais de instalação conhecidos).");
        }

        runner.StartDetached(exePath, "");
        logger?.LogInformation("'{ExeName}' iniciado (não estava rodando).", exeName);
        return Result.Ok();
    }

    private async Task<Result> ReloadOrStartAsync(
        string exeName, string reloadArgs, string startArgs, CancellationToken ct)
    {
        var exePath = resolver.Resolve(exeName);
        if (exePath is null)
        {
            logger?.LogWarning("'{ExeName}' não encontrado (nem no PATH nem nos locais de instalação conhecidos).", exeName);
            return Result.Fail($"'{exeName}' não encontrado (nem no PATH nem nos locais de instalação conhecidos).");
        }

        try
        {
            var result = await runner.RunAsync(exePath, reloadArgs, ct);
            if (result.ExitCode == 0)
            {
                logger?.LogInformation("Reload de '{ExePath} {ReloadArgs}' concluído.", exePath, reloadArgs);
                return Result.Ok();
            }

            // Exit code != 0 normalmente significa "não está rodando": sobe o app.
            logger?.LogInformation(
                "'{ExePath} {ReloadArgs}' saiu com código {ExitCode} — assumindo que não está rodando, iniciando com '{StartArgs}'.",
                exePath, reloadArgs, result.ExitCode, startArgs);
            runner.StartDetached(exePath, startArgs);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger?.LogError(ex, "Falha ao executar '{ExePath}'.", exePath);
            return Result.Fail($"Falha ao executar '{exePath}': {ex.Message}");
        }
    }
}
