using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Orquestrador transacional (RF-08): executa os steps em ordem; na primeira falha,
/// compensa os steps já executados (incluindo o que falhou) em ordem reversa.
/// A compensação roda mesmo após cancelamento — nunca deixar estado meio-aplicado.
/// </summary>
public sealed class InstallPipeline(IReadOnlyList<IInstallStep> steps)
{
    public async Task<Result> RunAsync(InstallContext context, CancellationToken ct = default)
    {
        var executed = new Stack<IInstallStep>();

        foreach (var step in steps)
        {
            Result result;
            try
            {
                result = await step.ExecuteAsync(context, ct);
            }
            catch (OperationCanceledException)
            {
                result = Result.Fail("Operação cancelada pelo usuário.");
            }
            catch (Exception ex)
            {
                result = Result.Fail($"{step.Name}: {ex.Message}");
            }

            executed.Push(step);
            if (result.IsSuccess)
                continue;

            context.Report($"❌ Falha em '{step.Name}'. Desfazendo alterações...");
            await CompensateAsync(executed, context);
            return Result.Fail(result.Error!);
        }

        return Result.Ok();
    }

    private static async Task CompensateAsync(Stack<IInstallStep> executed, InstallContext context)
    {
        while (executed.Count > 0)
        {
            var step = executed.Pop();
            try
            {
                await step.CompensateAsync(context);
            }
            catch (Exception ex)
            {
                context.Report($"⚠ Falha ao compensar '{step.Name}': {ex.Message}");
            }
        }
    }
}
