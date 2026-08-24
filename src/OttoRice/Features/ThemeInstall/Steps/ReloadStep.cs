using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OttoRice.AppRegistry;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Dispara o reload dos apps afetados. Falha de reload NÃO derruba o pipeline:
/// as configs já foram aplicadas com sucesso; o usuário pode reiniciar o app à mão.
/// </summary>
public sealed class ReloadStep(IAppReloader reloader) : IInstallStep
{
    public string Name => "Reload";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        var actions = context.Operations
            .Select(op => SupportedApps.All[op.Target.App!].Reload)
            .Where(action => action is not ReloadAction.None and not ReloadAction.Wallpaper)
            .Distinct();

        foreach (var action in actions)
        {
            context.Report($"Recarregando {action}...");
            var result = await reloader.ReloadAsync(action, ct);
            if (!result.IsSuccess)
                context.Report($"⚠ Reload de {action} falhou (config aplicada mesmo assim): {result.Error}");
        }

        return Result.Ok();
    }
}
