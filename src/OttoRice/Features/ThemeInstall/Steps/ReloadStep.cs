using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Dispara o reload dos apps afetados. Falha de reload NÃO derruba o pipeline:
/// as configs já foram aplicadas com sucesso; o usuário pode reiniciar o app à mão.
///
/// Validador pós-reload (RF geral de observabilidade): depois de disparar o reload/start,
/// confirma que o processo esperado (GlazeWM, YASB, Zebar, Flow Launcher — não Wallpaper/None)
/// está mesmo rodando, com espera curta e limitada (pode estar só devagar pra subir). Não falha
/// o pipeline por isso — é um warning reportado (Log/Progress), igual à falha de reload acima:
/// as configs já foram aplicadas com sucesso, então não vale a pena disparar rollback
/// transacional por um processo lento a subir.
/// </summary>
public sealed class ReloadStep(
    IAppReloader reloader,
    IProcessRunner runner,
    ILogger<ReloadStep>? logger = null,
    TimeSpan? verifyTimeout = null) : IInstallStep
{
    /// <summary>Injetável só para os testes encurtarem a espera — produção usa o default de 5s.</summary>
    private readonly TimeSpan _verifyTimeout = verifyTimeout ?? TimeSpan.FromSeconds(5);

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
            {
                logger?.LogWarning("Reload de {Action} falhou (config aplicada mesmo assim): {Error}", action, result.Error);
                context.Report($"⚠ Reload de {action} falhou (config aplicada mesmo assim): {result.Error}");
            }
            else
            {
                logger?.LogInformation("Reload de {Action} concluído.", action);
            }

            await VerifyRunningAsync(action, context, ct);
        }

        return Result.Ok();
    }

    /// <summary>Espera curta e limitada (poll de 200ms, até 5s) — não bloqueia o pipeline por muito tempo.</summary>
    private async Task VerifyRunningAsync(ReloadAction action, InstallContext context, CancellationToken ct)
    {
        var processName = reloader.ExpectedProcessName(action);
        if (processName is null)
            return;

        var deadline = DateTime.UtcNow + _verifyTimeout;
        while (runner.FindProcessIds(processName).Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(200, ct);

        if (runner.FindProcessIds(processName).Count == 0)
        {
            logger?.LogWarning(
                "'{ProcessName}' não está rodando após o reload/start de {Action} — o app pode não ter iniciado.",
                processName, action);
            context.Report($"⚠ '{processName}' não parece estar rodando depois de {action} — verifique manualmente.");
        }
    }
}
