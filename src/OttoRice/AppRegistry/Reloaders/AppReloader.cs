using System;
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
            ReloadAction.None or ReloadAction.Wallpaper => Result.Ok(),
            ReloadAction.TranslucentTb => RestartTranslucentTb(),
            _ => Result.Fail($"Ação de reload desconhecida: {action}"),
        };
    }

    /// <summary>
    /// TranslucentTB observa o settings.json em tempo real nas versões atuais, mas para
    /// manter o mesmo padrão previsível dos demais reloaders (e cobrir versões que não
    /// recarreguem sozinhas), encerra o processo por PID — nunca por nome em massa — e
    /// sobe uma nova instância a partir do executável resolvido.
    /// </summary>
    private Result RestartTranslucentTb()
    {
        var exePath = resolver.Resolve("TranslucentTB");
        if (exePath is null)
        {
            logger?.LogWarning("'TranslucentTB' não encontrado — verifique a instalação.");
            return Result.Fail("TranslucentTB não encontrado, verifique a instalação.");
        }

        foreach (var pid in runner.FindProcessIds("TranslucentTB"))
        {
            if (!runner.TryKill(pid))
                logger?.LogWarning("Falha ao encerrar TranslucentTB (pid {Pid}).", pid);
        }

        try
        {
            runner.StartDetached(exePath, string.Empty);
            logger?.LogInformation("TranslucentTB reiniciado a partir de '{ExePath}'.", exePath);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            logger?.LogError(ex, "Falha ao iniciar '{ExePath}'.", exePath);
            return Result.Fail($"Falha ao iniciar '{exePath}': {ex.Message}");
        }
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
