using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    IExecutableResolver resolver)
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

        // O wm-exit já derruba o Zebar via shutdown_commands na config padrão; se sobrou, encerra por PID.
        if (state.ManagedApps.Contains("zebar"))
            KillIfRunning("zebar", progress);

        if (state.ManagedApps.Contains("yasb"))
        {
            progress?.Invoke("Parando a barra YASB...");
            var stop = await TryRunAsync("yasbc", "stop --silent", ct);
            if (!stop.IsSuccess)
                KillIfRunning("yasb", progress);

            progress?.Invoke("Desabilitando o autostart do YASB (reversível)...");
            await TryRunAsync("yasbc", "disable-autostart", ct);
        }

        RestoreOriginalWallpaper(state, progress);

        await stateStore.WriteAsync(state with { IsEnabled = false }, ct);
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

        await stateStore.WriteAsync(state with { IsEnabled = true }, ct);
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
            progress?.Invoke($"⚠ Não foi possível restaurar o papel de parede: {ex.Message}");
        }
    }

    private void KillIfRunning(string processName, Action<string>? progress)
    {
        if (!KillableProcesses.Contains(processName))
            throw new InvalidOperationException($"Processo '{processName}' fora da whitelist de encerramento.");

        foreach (var pid in runner.FindProcessIds(processName))
        {
            progress?.Invoke($"Encerrando {processName} (pid {pid})...");
            runner.TryKill(pid);
        }
    }

    private async Task<Result> TryRunAsync(string fileName, string arguments, CancellationToken ct)
    {
        try
        {
            var exePath = resolver.Resolve(fileName) ?? fileName;
            var result = await runner.RunAsync(exePath, arguments, ct);
            return result.ExitCode == 0
                ? Result.Ok()
                : Result.Fail($"'{fileName} {arguments}' saiu com código {result.ExitCode}.");
        }
        catch (Exception ex)
        {
            return Result.Fail($"'{fileName}' não encontrado no PATH: {ex.Message}");
        }
    }
}
