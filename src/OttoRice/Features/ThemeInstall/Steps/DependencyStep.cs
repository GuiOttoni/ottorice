using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Instala dependências ausentes via WinGet, em série (lock do WinGet).
/// Sem compensação: desinstalar pacotes num rollback seria mais destrutivo que deixá-los
/// (podem ser compartilhados por outros temas); ficam registrados no contexto para
/// a futura desinstalação explícita.
/// </summary>
public sealed class DependencyStep(IWinGetClient winGet, ILogger<DependencyStep>? logger = null) : IInstallStep
{
    public string Name => "Dependências";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        if (context.Manifest.Dependencies.Count > 0 && !await winGet.IsAvailableAsync(ct))
        {
            logger?.LogWarning("WinGet indisponível — {Count} dependência(s) não puderam ser verificadas.",
                context.Manifest.Dependencies.Count);
            return Result.Fail("WinGet não está disponível nesta máquina (instale o App Installer da Microsoft Store).");
        }

        foreach (var dependency in context.Manifest.Dependencies)
        {
            var id = dependency.WingetId!;
            if (await winGet.IsInstalledAsync(id, ct))
            {
                context.Report($"{id} já instalado.");
                continue;
            }

            context.Report($"Instalando {id} via WinGet (pode demorar)...");
            var result = await winGet.InstallAsync(id, ct);
            if (!result.IsSuccess)
            {
                logger?.LogError("Falha ao instalar dependência '{Id}': {Error}", id, result.Error);
                return result;
            }
            logger?.LogInformation("Dependência '{Id}' instalada via WinGet.", id);
            context.WingetIdsInstalled.Add(id);
        }

        return Result.Ok();
    }
}
