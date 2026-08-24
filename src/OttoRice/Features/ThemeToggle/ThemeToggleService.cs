using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeToggle;

/// <summary>
/// Liga/desliga o tema ativo sem desinstalar nada (RF-15).
///
/// Comandos verificados (ago/2026): GlazeWM v3 `glazewm command wm-exit`,
/// `wm-toggle-pause`, `glazewm start --config`; YASB `yasbc start|stop --silent` e
/// `enable-autostart|disable-autostart`. O Zebar não tem stop por CLI — é encerrado
/// pelos shutdown_commands do GlazeWM; o kill por PID abaixo é fallback, restrito à
/// whitelist de nomes de processo (nunca kill por nome em massa).
///
/// Limitação conhecida a comunicar: ao sair, o GlazeWM devolve as janelas visíveis mas
/// NÃO restaura posições/tamanhos originais.
/// </summary>
public sealed class ThemeToggleService(
    IProcessRunner runner,
    IWallpaperService wallpaper,
    ThemeStateStore stateStore,
    IExecutableResolver resolver,
    ILogger<ThemeToggleService>? logger = null)
{
    /// <summary>Únicos processos que este serviço pode encerrar por PID.</summary>
    private static readonly HashSet<string> KillableProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "zebar", "yasb" };

    public Task<ThemeState> GetStateAsync(CancellationToken ct = default) => stateStore.ReadAsync(ct);

    /// <summary>Pausa/retoma o tiling do GlazeWM sem derrubar nada (toggle leve).</summary>
    public async Task<Result> TogglePauseAsync(CancellationToken ct = default)
    {
        var result = await TryRunAsync("glazewm", "command wm-toggle-pause", ct);
        return result.IsSuccess
            ? Result.Ok()
            : Result.Fail($"Não foi possível pausar o GlazeWM: {result.Error}");
    }

    public async Task<Result> TurnOffAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        var state = await stateStore.ReadAsync(ct);
        if (!state.HasActiveTheme)
            return Result.Fail("Nenhum tema ativo para desligar.");
        if (!state.IsEnabled)
            return Result.Fail("O tema já está desligado.");

        if (state.ManagedApps.Contains("glazewm"))
        {
            progress?.Invoke("Encerrando GlazeWM (as janelas voltam a ficar visíveis, mas não às posições originais)...");
            await TryRunAsync("glazewm", "command wm-exit", ct);
        }

        // De propósito: Flow Launcher (se gerenciado) não é encerrado aqui — só é iniciado em
        // TurnOnAsync. Ver comentário lá.

        // O wm-exit já derruba o Zebar via shutdown_commands na config padrão; se sobrou, encerra por PID.
        if (state.ManagedApps.Contains("zebar"))
            KillIfRunning("zebar", progress);

        if (state.ManagedApps.Contains("yasb"))
        {
            progress?.Invoke("Parando a barra YASB...");
            var stop = await TryRunAsync("yasbc", "stop --silent", ct);
            if (!stop.IsSuccess)
                KillIfRunning("yasb", progress);

            // yasbc stop devolve antes do processo terminar de fato (soltar o lock de
            // instância única e desregistrar o AppBar). Sem esperar, um TurnOn logo em
            // seguida colide com o processo antigo ainda saindo: a nova instância vê
            // "Another instance of the YASB is already running", aborta, e a barra some
            // até o próximo reload manual — foi exatamente isso que aconteceu numa
            // instalação real (yasb.log mostra stop→start com ~3s de intervalo e a
            // segunda instância se autoencerrando).
            await WaitForProcessExitAsync("yasb", TimeSpan.FromSeconds(5), ct);

            progress?.Invoke("Desabilitando o autostart do YASB (reversível)...");
            await TryRunAsync("yasbc", "disable-autostart", ct);
        }

        RestoreOriginalWallpaper(state, progress);

        await stateStore.WriteAsync(state with { IsEnabled = false }, ct);
        logger?.LogInformation("Tema {ThemeId} desligado.", state.ActiveThemeId);
        return Result.Ok();
    }

    public async Task<Result> TurnOnAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        var state = await stateStore.ReadAsync(ct);
        if (!state.HasActiveTheme)
            return Result.Fail("Nenhum tema ativo para ligar.");
        if (state.IsEnabled)
            return Result.Fail("O tema já está ligado.");

        if (state.ThemeWallpaperPath is not null && File.Exists(state.ThemeWallpaperPath))
        {
            progress?.Invoke("Aplicando o papel de parede do tema...");
            wallpaper.Set(state.ThemeWallpaperPath);
        }

        if (state.ManagedApps.Contains("glazewm"))
        {
            var glazewm = resolver.Resolve("glazewm");
            if (glazewm is null)
                return Result.Fail("GlazeWM não encontrado — reinstale o tema ou verifique a instalação.");

            progress?.Invoke("Iniciando GlazeWM...");
            var args = state.GlazeWmConfigPath is not null
                ? $"start --config \"{state.GlazeWmConfigPath}\""
                : "start";
            runner.StartDetached(glazewm, args);
        }

        if (state.ManagedApps.Contains("yasb"))
        {
            progress?.Invoke("Iniciando a barra YASB...");
            var yasbc = resolver.Resolve("yasbc");
            if (yasbc is not null)
            {
                runner.StartDetached(yasbc, "start --silent");
                await TryRunAsync("yasbc", "enable-autostart", ct);
            }
            else
            {
                progress?.Invoke("⚠ YASB não encontrado — barra não iniciada.");
            }
        }

        // Flow Launcher só é INICIADO no ligar — nunca encerrado no desligar (ver TurnOffAsync).
        // Diferente do GlazeWM/YASB, ele não é visualmente disruptivo de deixar rodando (não
        // tila janelas nem ocupa uma barra de tela) — matá-lo a cada toggle off só derrubaria
        // o launcher do usuário sem necessidade.
        if (state.ManagedApps.Contains("flow_launcher") && runner.FindProcessIds("Flow.Launcher").Count == 0)
        {
            var flowLauncher = resolver.Resolve("Flow.Launcher");
            if (flowLauncher is not null)
            {
                progress?.Invoke("Iniciando o Flow Launcher...");
                runner.StartDetached(flowLauncher, "");
            }
            else
            {
                progress?.Invoke("⚠ Flow Launcher não encontrado — não iniciado.");
            }
        }

        await stateStore.WriteAsync(state with { IsEnabled = true }, ct);
        logger?.LogInformation("Tema {ThemeId} ligado.", state.ActiveThemeId);
        return Result.Ok();
    }

    private void RestoreOriginalWallpaper(ThemeState state, Action<string>? progress)
    {
        // A cópia local tem prioridade: o caminho original pode ter sumido ou ser cache do Spotlight.
        var source = state.OriginalWallpaperCopy is not null && File.Exists(state.OriginalWallpaperCopy)
            ? state.OriginalWallpaperCopy
            : state.OriginalWallpaperPath is not null && File.Exists(state.OriginalWallpaperPath)
                ? state.OriginalWallpaperPath
                : null;

        if (source is null)
            return;

        progress?.Invoke("Restaurando o papel de parede anterior...");
        try
        {
            wallpaper.Set(source);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Não foi possível restaurar o papel de parede anterior.");
            progress?.Invoke($"⚠ Não foi possível restaurar o papel de parede: {ex.Message}");
        }
    }

    private async Task WaitForProcessExitAsync(string processName, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (runner.FindProcessIds(processName).Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(200, ct);

        if (runner.FindProcessIds(processName).Count > 0)
            logger?.LogWarning("'{ProcessName}' ainda rodando após {Timeout}s de espera pelo encerramento.", processName, timeout.TotalSeconds);
    }

    private void KillIfRunning(string processName, Action<string>? progress)
    {
        if (!KillableProcesses.Contains(processName))
            throw new InvalidOperationException($"Processo '{processName}' fora da whitelist de encerramento.");

        foreach (var pid in runner.FindProcessIds(processName))
        {
            progress?.Invoke($"Encerrando {processName} (pid {pid})...");
            if (!runner.TryKill(pid))
                logger?.LogWarning("Falha ao encerrar {ProcessName} (pid {Pid}).", processName, pid);
        }
    }

    private async Task<Result> TryRunAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var exePath = resolver.Resolve(fileName) ?? fileName;
            var result = await runner.RunAsync(exePath, arguments, ct);
            if (result.ExitCode != 0)
                logger?.LogWarning(
                    "'{FileName} {Arguments}' saiu com código {ExitCode}: {StdErr}",
                    fileName, arguments, result.ExitCode, result.StandardError);
            return result.ExitCode == 0
                ? Result.Ok()
                : Result.Fail($"'{fileName} {arguments}' saiu com código {result.ExitCode}.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "'{FileName}' não encontrado no PATH.", fileName);
            return Result.Fail($"'{fileName}' não encontrado no PATH: {ex.Message}");
        }
    }
}
