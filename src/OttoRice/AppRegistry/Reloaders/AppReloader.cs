using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Reloaders;

/// <summary>
/// Reload dos apps da whitelist. Comandos fixos — nada vem do manifesto.
/// Se o app não está rodando (comando de reload falha), tenta iniciá-lo detached.
/// </summary>
public sealed class AppReloader(IProcessRunner runner) : IAppReloader
{
    public async Task<Result> ReloadAsync(ReloadAction action, CancellationToken ct = default)
    {
        return action switch
        {
            ReloadAction.GlazeWm => await ReloadOrStartAsync("glazewm", "command wm-reload-config", "glazewm", "", ct),
            ReloadAction.Yasb => await ReloadOrStartAsync("yasbc", "reload", "yasbc", "start", ct),
            // Zebar é iniciado/gerenciado junto do GlazeWM (startup do próprio WM).
            ReloadAction.Zebar => Result.Ok(),
            ReloadAction.None or ReloadAction.Wallpaper => Result.Ok(),
            _ => Result.Fail($"Ação de reload desconhecida: {action}"),
        };
    }

    private async Task<Result> ReloadOrStartAsync(
        string reloadExe, string reloadArgs, string startExe, string startArgs, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(reloadExe, reloadArgs, ct);
            if (result.ExitCode == 0)
                return Result.Ok();

            runner.StartDetached(startExe, startArgs);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Executável não encontrado no PATH — instalação recém-feita pode exigir novo shell/logon.
            return Result.Fail($"'{reloadExe}' não encontrado no PATH: {ex.Message}");
        }
    }
}
