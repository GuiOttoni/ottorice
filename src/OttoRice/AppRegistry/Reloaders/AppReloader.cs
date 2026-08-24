using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Reloaders;

/// <summary>
/// Reload dos apps da whitelist. Comandos fixos — nada vem do manifesto.
/// Se o app não está rodando (o reload falha), ele é iniciado.
/// Os executáveis são resolvidos por caminho absoluto: GlazeWM/YASB instalam em
/// Program Files sem entrar no PATH, e o PATH do processo é anterior à instalação.
/// </summary>
public sealed class AppReloader(IProcessRunner runner, IExecutableResolver resolver) : IAppReloader
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
            _ => Result.Fail($"Ação de reload desconhecida: {action}"),
        };
    }

    private async Task<Result> ReloadOrStartAsync(
        string exeName, string reloadArgs, string startArgs, CancellationToken ct)
    {
        var exePath = resolver.Resolve(exeName);
        if (exePath is null)
            return Result.Fail($"'{exeName}' não encontrado (nem no PATH nem nos locais de instalação conhecidos).");

        try
        {
            var result = await runner.RunAsync(exePath, reloadArgs, ct);
            if (result.ExitCode == 0)
                return Result.Ok();

            // Exit code != 0 normalmente significa "não está rodando": sobe o app.
            runner.StartDetached(exePath, startArgs);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Result.Fail($"Falha ao executar '{exePath}': {ex.Message}");
        }
    }
}
