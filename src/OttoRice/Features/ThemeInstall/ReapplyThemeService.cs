using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Reaplica o tema ativo sem passar pela instalação completa (RF-08 continua sendo o
/// InstallPipeline "cheio", usado só na primeira instalação). Pipeline enxuta —
/// [Planejamento, Aplicação, Reload, Mods do Windhawk] — recebida pronta do DI
/// (App.axaml.cs monta a lista reduzida de steps, pulando Dependência e Backup):
///   - Dependência é redundante: as ferramentas já foram instaladas na primeira vez.
///   - Backup é pulado de propósito: criar uma sessão nova a cada reaplicação poluiria o
///     BackupSessionStore e o InstallHistoryStore (que assume um registro por tema —
///     ver UninstallService.UninstallAsync/FirstOrDefault) sem necessidade, já que o
///     rollback correto continua sendo a sessão de backup da instalação original.
///
/// Rebaixa os arquivos do tema de novo via ThemeFetcher (em vez de reaproveitar o diretório
/// de cache antigo) — mais simples e garante que o conteúdo bate com o repo/pasta atual,
/// ao custo de precisar de rede para temas de origem GitHub.
///
/// Sem BackupStep, uma falha no meio da Aplicação não tem compensação automática — risco
/// aceito e documentado (ver seção 12.2 do plano de evolução, docs/ottorice.md): o conteúdo
/// sendo escrito é o do mesmo tema já instalado, então uma falha parcial deixa o sistema
/// "quase igual" ao que já estava, não corrompido por um tema estranho.
/// </summary>
public sealed class ReapplyThemeService(
    IThemeFetcher fetcher,
    InstallPipeline pipeline,
    ThemeStateStore stateStore,
    ILogger<ReapplyThemeService>? logger = null)
{
    public async Task<Result> ReapplyAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        var state = await stateStore.ReadAsync(ct);
        if (!state.HasActiveTheme)
            return Result.Fail("Nenhum tema ativo para reaplicar.");
        if (string.IsNullOrEmpty(state.SourceUrl))
            return Result.Fail(
                "Este tema não tem uma origem salva (foi instalado antes desta versão do OttoRice) — reinstale pela aba Instalar.");

        progress?.Invoke("Baixando os arquivos do tema novamente...");
        var fetched = await fetcher.FetchAsync(state.SourceUrl, ct);
        if (!fetched.IsSuccess)
            return Result.Fail($"Falha ao baixar o tema: {fetched.Error}");

        var context = new InstallContext
        {
            Manifest = fetched.Value!.Manifest,
            ThemeDirectory = fetched.Value.ThemeDirectory,
            Progress = progress,
        };

        var result = await pipeline.RunAsync(context, ct);
        if (!result.IsSuccess)
            return result;

        // Atualiza os caminhos derivados (podem ter mudado se o conteúdo do tema mudou),
        // preservando o que só a instalação/backup original sabe (wallpaper anterior etc.).
        var wallpaperOp = context.Operations.FirstOrDefault(op => op.Target.Action == "set");
        var glazeOp = context.Operations.FirstOrDefault(op => op.Target.App == "glazewm");
        await stateStore.WriteAsync(state with
        {
            ThemeWallpaperPath = wallpaperOp?.SourcePath ?? state.ThemeWallpaperPath,
            GlazeWmConfigPath = glazeOp?.TargetPath ?? state.GlazeWmConfigPath,
            ManagedApps = context.Operations.Count > 0
                ? [.. context.Operations.Select(op => op.Target.App!).Distinct()]
                : state.ManagedApps,
        }, ct);

        logger?.LogInformation("Tema {ThemeId} reaplicado.", state.ActiveThemeId);
        return Result.Ok();
    }
}
