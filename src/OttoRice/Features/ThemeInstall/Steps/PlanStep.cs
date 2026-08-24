using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>Resolve os targets do manifesto em operações concretas antes de tocar qualquer coisa.</summary>
public sealed class PlanStep(TargetPlanner planner, ILogger<PlanStep>? logger = null) : IInstallStep
{
    public string Name => "Planejamento";

    public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        var plan = planner.Build(context.Manifest, context.ThemeDirectory);
        if (!plan.IsSuccess)
        {
            logger?.LogWarning("Planejamento falhou: {Error}", plan.Error);
            return Task.FromResult(Result.Fail(plan.Error!));
        }

        context.Operations.Clear();
        context.Operations.AddRange(plan.Value!);
        logger?.LogInformation("{Count} operação(ões) de arquivo planejada(s).", context.Operations.Count);
        context.Report($"{context.Operations.Count} operação(ões) de arquivo planejada(s).");
        return Task.FromResult(Result.Ok());
    }
}
