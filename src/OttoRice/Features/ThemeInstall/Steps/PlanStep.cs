using System.Linq;
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
        // Toggle por componente: se o usuário desmarcou algum target na prévia, os índices
        // selecionados chegam aqui e só esses viram operação — os demais nunca são planejados
        // nem aplicados. null = todos (comportamento padrão).
        var targets = context.SelectedTargetIndexes is null
            ? context.Manifest.Targets
            : context.Manifest.Targets
                .Where((_, i) => context.SelectedTargetIndexes.Contains(i))
                .ToList();

        string? paletteSourceOverride = null;
        if (context.PaletteId is not null)
        {
            var palette = context.Manifest.Palettes.FirstOrDefault(p => p.Id == context.PaletteId);
            if (palette is null)
                logger?.LogWarning(
                    "Paleta '{PaletteId}' não existe mais no manifesto do tema '{ThemeId}' — aplicando a paleta padrão.",
                    context.PaletteId, context.Manifest.ThemeId);
            paletteSourceOverride = palette?.SourceOverride;
        }

        var plan = planner.Build(context.Manifest, context.ThemeDirectory, targets, paletteSourceOverride);
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
