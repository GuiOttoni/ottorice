using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;
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
    /// <summary>
    /// Busca de novo os targets do tema instalado, sem aplicar nada — usado pra popular o
    /// checkbox de toggle por componente na UI de reaplicação antes de confirmar (evita reusar
    /// um manifesto potencialmente desatualizado: o conteúdo do tema pode ter mudado desde a
    /// instalação/última reaplicação).
    /// </summary>
    public async Task<Result<IReadOnlyList<RiceTarget>>> FetchTargetsAsync(
        string themeId, CancellationToken ct = default)
    {
        var installed = await stateStore.ReadAsync(ct);
        if (!installed.Themes.TryGetValue(themeId, out var state))
            return Result<IReadOnlyList<RiceTarget>>.Fail($"Tema '{themeId}' não está instalado.");
        if (string.IsNullOrEmpty(state.SourceUrl))
            return Result<IReadOnlyList<RiceTarget>>.Fail(
                "Este tema não tem uma origem salva (foi instalado antes desta versão do OttoRice) — reinstale pela aba Instalar.");

        var fetched = await fetcher.FetchAsync(state.SourceUrl, ct);
        if (!fetched.IsSuccess)
            return Result<IReadOnlyList<RiceTarget>>.Fail($"Falha ao baixar o tema: {fetched.Error}");

        return Result<IReadOnlyList<RiceTarget>>.Ok(fetched.Value!.Manifest.Targets);
    }

    /// <summary>
    /// Busca de novo as paletas de cores alternativas declaradas pelo tema (seção 13 da doc
    /// "OttoRice") — usado para popular o seletor de paleta na UI. Lista vazia = tema sem
    /// paletas alternativas (não é erro).
    /// </summary>
    public async Task<Result<IReadOnlyList<RicePalette>>> FetchPalettesAsync(
        string themeId, CancellationToken ct = default)
    {
        var installed = await stateStore.ReadAsync(ct);
        if (!installed.Themes.TryGetValue(themeId, out var state))
            return Result<IReadOnlyList<RicePalette>>.Fail($"Tema '{themeId}' não está instalado.");
        if (string.IsNullOrEmpty(state.SourceUrl))
            return Result<IReadOnlyList<RicePalette>>.Fail(
                "Este tema não tem uma origem salva (foi instalado antes desta versão do OttoRice) — reinstale pela aba Instalar.");

        var fetched = await fetcher.FetchAsync(state.SourceUrl, ct);
        if (!fetched.IsSuccess)
            return Result<IReadOnlyList<RicePalette>>.Fail($"Falha ao baixar o tema: {fetched.Error}");

        return Result<IReadOnlyList<RicePalette>>.Ok(fetched.Value!.Manifest.Palettes);
    }

    /// <summary>Reaplica o tema indicado (qualquer tema instalado, não só o ativo — seção 12.3
    /// do plano de evolução generalizou este método para N temas).</summary>
    /// <param name="selectedTargetIndexes">
    /// Índices (na ordem de <see cref="RiceManifest.Targets"/> do manifesto rebaixado) dos
    /// componentes a reaplicar desta vez — toggle por componente (mesma ideia da instalação:
    /// ex. reaplicar só o wallpaper sem mexer no YASB de novo). <c>null</c> = todos (padrão).
    /// </param>
    public async Task<Result> ReapplyAsync(
        string themeId,
        Action<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlySet<int>? selectedTargetIndexes = null)
    {
        var installed = await stateStore.ReadAsync(ct);
        if (!installed.Themes.TryGetValue(themeId, out var state))
            return Result.Fail($"Tema '{themeId}' não está instalado.");
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
            SelectedTargetIndexes = selectedTargetIndexes,
            // Reaplicação "normal" (botão REAPLICAR) preserva a paleta ativa no momento — não
            // reseta silenciosamente pra padrão. Trocar de paleta de propósito é ApplyPaletteAsync.
            PaletteId = state.ActivePaletteId,
        };

        var result = await pipeline.RunAsync(context, ct);
        if (!result.IsSuccess)
            return result;

        await PersistDerivedStateAsync(state, context, ct);

        logger?.LogInformation("Tema {ThemeId} reaplicado.", themeId);
        return Result.Ok();
    }

    /// <summary>
    /// Troca a paleta de cores ativa de um tema já instalado (seção 13 da doc "OttoRice") —
    /// reaproveita o mesmo pipeline reduzido da reaplicação, só apontando o
    /// <see cref="TargetPlanner"/> pra resolver a partir do diretório da paleta em vez do
    /// padrão. Reaplica sempre todos os targets do tema (sem toggle por componente): trocar de
    /// paleta precisa reescrever/restaurar qualquer arquivo que a paleta anterior tenha tocado,
    /// não só um subconjunto.
    /// </summary>
    /// <param name="paletteId">
    /// Id de uma das <see cref="RiceManifest.Palettes"/> do tema, ou <c>null</c> para voltar à
    /// paleta padrão (<c>configs/</c>, sem override).
    /// </param>
    public async Task<Result> ApplyPaletteAsync(
        string themeId, string? paletteId, Action<string>? progress = null, CancellationToken ct = default)
    {
        var installed = await stateStore.ReadAsync(ct);
        if (!installed.Themes.TryGetValue(themeId, out var state))
            return Result.Fail($"Tema '{themeId}' não está instalado.");
        if (string.IsNullOrEmpty(state.SourceUrl))
            return Result.Fail(
                "Este tema não tem uma origem salva (foi instalado antes desta versão do OttoRice) — reinstale pela aba Instalar.");

        progress?.Invoke("Baixando os arquivos do tema novamente...");
        var fetched = await fetcher.FetchAsync(state.SourceUrl, ct);
        if (!fetched.IsSuccess)
            return Result.Fail($"Falha ao baixar o tema: {fetched.Error}");

        if (paletteId is not null && fetched.Value!.Manifest.Palettes.All(p => p.Id != paletteId))
            return Result.Fail($"Paleta '{paletteId}' não existe mais no manifesto deste tema.");

        var context = new InstallContext
        {
            Manifest = fetched.Value!.Manifest,
            ThemeDirectory = fetched.Value.ThemeDirectory,
            Progress = progress,
            SelectedTargetIndexes = null,
            PaletteId = paletteId,
        };

        var result = await pipeline.RunAsync(context, ct);
        if (!result.IsSuccess)
            return result;

        await PersistDerivedStateAsync(state with { ActivePaletteId = paletteId }, context, ct);

        logger?.LogInformation("Paleta '{PaletteId}' aplicada ao tema {ThemeId}.", paletteId ?? "(padrão)", themeId);
        return Result.Ok();
    }

    /// <summary>Atualiza os caminhos derivados (podem ter mudado se o conteúdo do tema mudou),
    /// preservando o que só a instalação/backup original sabe (wallpaper anterior etc.).</summary>
    private Task PersistDerivedStateAsync(ThemeState state, InstallContext context, CancellationToken ct)
    {
        var wallpaperOp = context.Operations.FirstOrDefault(op => op.Target.Action == "set");
        var glazeOp = context.Operations.FirstOrDefault(op => op.Target.App == "glazewm");
        return stateStore.UpsertThemeAsync(state with
        {
            ThemeWallpaperPath = wallpaperOp?.SourcePath ?? state.ThemeWallpaperPath,
            GlazeWmConfigPath = glazeOp?.TargetPath ?? state.GlazeWmConfigPath,
            ManagedApps = context.Operations.Count > 0
                ? [.. context.Operations.Select(op => op.Target.App!).Distinct()]
                : state.ManagedApps,
        }, ct: ct);
    }
}
