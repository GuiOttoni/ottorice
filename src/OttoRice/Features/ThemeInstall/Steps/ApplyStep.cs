using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry.Appliers;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Executa as operações planejadas. Sem compensação própria: a restauração de
/// arquivos e do wallpaper é responsabilidade do BackupStep.
/// </summary>
public sealed class ApplyStep(
    FileOverrideApplier overrideApplier,
    WindowsTerminalApplier terminalApplier,
    IWallpaperService wallpaper,
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

        logger?.LogInformation(
            "Aplicação concluída: {Count} operação(ões) para o tema {ThemeId}.",
            context.Operations.Count, context.Manifest.ThemeId);
        return Result.Ok();
    }
}
