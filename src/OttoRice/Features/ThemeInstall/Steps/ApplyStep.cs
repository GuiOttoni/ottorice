using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry.Appliers;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Executa as operações planejadas. Sem compensação própria: a restauração de
/// arquivos, wallpaper e taskbar é responsabilidade do BackupStep.
/// </summary>
public sealed class ApplyStep(
    FileOverrideApplier overrideApplier,
    WindowsTerminalApplier terminalApplier,
    IWallpaperService wallpaper,
    ITaskbarService taskbar,
    ILogger<ApplyStep>? logger = null) : IInstallStep
{
    public string Name => "Aplicação";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        foreach (var op in context.Operations)
        {
            switch (op.Target.Action)
            {
                case "override":
                    context.Report($"Aplicando {Path.GetFileName(op.TargetPath)} ({op.Target.App})...");
                    await overrideApplier.ApplyAsync(op.SourcePath, op.TargetPath, ct);
                    break;

                case "merge_scheme":
                    context.Report("Injetando esquema de cores no Windows Terminal...");
                    var schemeJson = await File.ReadAllTextAsync(op.SourcePath, ct);
                    await terminalApplier.InjectColorSchemeAsync(
                        op.TargetPath, schemeJson, op.Target.SetAsDefault, ct);
                    break;

                case "set":
                    context.Report("Definindo papel de parede...");
                    wallpaper.Set(op.SourcePath);
                    break;
            }
        }

        // GlazeWM substitui o papel da taskbar nativa: ocultá-la automaticamente evita
        // que ela apareça por cima/ao lado do tiling do tema recém-instalado.
        if (context.Operations.Any(op => op.Target.App == "glazewm"))
        {
            context.Report("Ocultando a barra de tarefas nativa (auto-hide)...");
            taskbar.SetAutoHide(true);
        }

        logger?.LogInformation(
            "Aplicação concluída: {Count} operação(ões) para o tema {ThemeId}.",
            context.Operations.Count, context.Manifest.ThemeId);
        return Result.Ok();
    }
}
