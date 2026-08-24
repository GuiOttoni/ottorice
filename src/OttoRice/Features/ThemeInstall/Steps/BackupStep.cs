using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Snapshot de tudo que será tocado: arquivos de config (sessão de backup) e o
/// wallpaper atual. A compensação desta etapa é o rollback do pipeline inteiro.
/// </summary>
public sealed class BackupStep(
    BackupSessionStore store,
    IWallpaperService wallpaper,
    ILogger<BackupStep>? logger = null) : IInstallStep
{
    public string Name => "Backup";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        var targetFiles = context.Operations
            .Where(op => op.TargetPath.Length > 0)
            .Select(op => op.TargetPath);

        context.BackupSession = await store.CreateSessionAsync(
            context.Manifest.ThemeId!, targetFiles, ct);

        if (context.Operations.Any(op => op.Target.Action == "set"))
            context.PreviousWallpaperPath = wallpaper.GetCurrentPath();

        logger?.LogInformation("Backup criado (sessão {SessionId}).", context.BackupSession.Id);
        context.Report($"Backup criado (sessão {context.BackupSession.Id}).");
        return Result.Ok();
    }

    public async Task CompensateAsync(InstallContext context)
    {
        if (context.BackupSession is not null)
        {
            context.Report("Restaurando configurações do backup...");
            await store.RestoreAsync(context.BackupSession.Id);
        }

        if (context.PreviousWallpaperPath is not null)
            wallpaper.Set(context.PreviousWallpaperPath);

        logger?.LogInformation("Backup da sessão {SessionId} compensado (rollback).", context.BackupSession?.Id);
    }
}
