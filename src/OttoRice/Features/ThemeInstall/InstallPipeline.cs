using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Orquestrador transacional (RF-08): executa os steps em ordem; na primeira falha,
/// compensa os steps já executados (incluindo o que falhou) em ordem reversa.
/// A compensação roda mesmo após cancelamento — nunca deixar estado meio-aplicado.
/// </summary>
public sealed class InstallPipeline(IReadOnlyList<IInstallStep> steps, ILogger<InstallPipeline>? logger = null)
{
    /// <summary>Nomes dos steps na ordem de execução — pra UI pré-popular a visualização antes de rodar.</summary>
    public IReadOnlyList<string> StepNames { get; } = [.. steps.Select(s => s.Name)];

    public async Task<Result> RunAsync(InstallContext context, CancellationToken ct = default)
    {
        var executed = new Stack<IInstallStep>();

        foreach (var step in steps)
        {
            Result result;
            logger?.LogInformation("Step '{Step}' iniciado (tema {ThemeId}).", step.Name, context.Manifest.ThemeId);
            context.StepStateChanged?.Invoke(step.Name, InstallStepState.Running);
            try
            {
                result = await step.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("Step '{Step}' cancelado pelo usuário.", step.Name);
                result = Result.Fail("Operação cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Step '{Step}' lançou exceção não tratada.", step.Name);
                result = Result.Fail($"{step.Name}: {ex.Message}");
            }

            executed.Push(step);
            if (result.IsSuccess)
            {
                logger?.LogInformation("Step '{Step}' concluído.", step.Name);
                context.StepStateChanged?.Invoke(step.Name, InstallStepState.Success);
                continue;
            }

            logger?.LogError("Step '{Step}' falhou: {Error}. Desfazendo alterações...", step.Name, result.Error);
            context.StepStateChanged?.Invoke(step.Name, InstallStepState.Failed);
            context.Report($"❌ Falha em '{step.Name}'. Desfazendo alterações...");
            await CompensateAsync(executed, context, logger, failedStep: step);
            return Result.Fail(result.Error!);
        }

        return Result.Ok();
    }

    private static async Task CompensateAsync(
        Stack<IInstallStep> executed, InstallContext context, ILogger<InstallPipeline>? logger, IInstallStep failedStep)
    {
        while (executed.Count > 0)
        {
            var step = executed.Pop();
            try
            {
                await step.CompensateAsync(context);
                logger?.LogInformation("Compensação de '{Step}' concluída.", step.Name);
                // O step que falhou fica com o estado "Falhou" (X vermelho) — só os que
                // tinham dado certo antes dele viram "Desfeito" (rollback).
                if (step != failedStep)
                    context.StepStateChanged?.Invoke(step.Name, InstallStepState.Compensated);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Falha ao compensar '{Step}'.", step.Name);
                context.Report($"⚠ Falha ao compensar '{step.Name}': {ex.Message}");
            }
        }
    }
}
