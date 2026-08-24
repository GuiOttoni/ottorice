using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Instala dependências ausentes via WinGet, em série (lock do WinGet).
/// Sem compensação: desinstalar pacotes num rollback seria mais destrutivo que deixá-los
/// (podem ser compartilhados por outros temas); ficam registrados no contexto para
/// a futura desinstalação explícita.
/// </summary>
public sealed class DependencyStep(IWinGetClient winGet) : IInstallStep
{
    public string Name => "Dependências";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        if (context.Manifest.Dependencies.Count > 0 && !await winGet.IsAvailableAsync(ct))
            return Result.Fail("WinGet não está disponível nesta máquina (instale o App Installer da Microsoft Store).");

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
                return result;
            context.WingetIdsInstalled.Add(id);
        }

        return Result.Ok();
    }
}
