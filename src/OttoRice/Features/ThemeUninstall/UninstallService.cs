using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Features.ThemeUninstall;

/// <summary>Uma ferramenta que o tema trouxe, com quantos outros temas ainda dependem dela.</summary>
public sealed record RemovableTool(string WingetId, int OtherThemesUsing)
{
    public bool IsSafeToRemove => OtherThemesUsing == 0;
}

/// <summary>
/// Desinstalação de tema (RF-16): desliga → restaura configs do backup → remove o
/// registro; a remoção dos binários é opcional e só é oferecida para ferramentas com
/// contagem de referência zero (nenhum outro tema instalado depende delas).
/// Como no Omarchy, o tema ativo é desligado antes de ser removido.
/// </summary>
public sealed class UninstallService(
    InstallHistoryStore history,
    BackupSessionStore backups,
    ThemeStateStore stateStore,
    ThemeToggleService toggle,
    IWinGetClient winGet,
    ILogger<UninstallService>? logger = null)
{
    /// <summary>Quais ferramentas do tema poderiam ser desinstaladas, e quantos temas ainda as usam.</summary>
    public async Task<IReadOnlyList<RemovableTool>> GetRemovableToolsAsync(
        string themeId, CancellationToken ct = default)
    {
        var records = await history.ReadAllAsync(ct);
        var target = records.FirstOrDefault(r => r.ThemeId == themeId);
        if (target is null)
            return [];

        return [.. target.WingetIdsInstalled.Distinct().Select(id => new RemovableTool(
            id,
            records.Count(r => r.ThemeId != themeId && r.WingetIdsInstalled.Contains(id))))];
    }

    public async Task<Result> UninstallAsync(
        string themeId,
        IReadOnlyCollection<string>? wingetIdsToRemove = null,
        Action<string>? progress = null,
        CancellationToken ct = default)
    {
        var record = (await history.ReadAllAsync(ct)).FirstOrDefault(r => r.ThemeId == themeId);
        if (record is null)
            return Result.Fail($"Tema '{themeId}' não consta no histórico de instalações.");

        var installed = await stateStore.ReadAsync(ct);
        if (installed.Themes.TryGetValue(themeId, out var themeState) && themeState.IsEnabled)
        {
            progress?.Invoke("Desligando o tema antes de remover...");
            var off = await toggle.TurnOffAsync(themeId, progress, ct);
            if (!off.IsSuccess)
                return Result.Fail($"Não foi possível desligar o tema: {off.Error}");
        }

        if (!string.IsNullOrEmpty(record.BackupSessionId))
        {
            progress?.Invoke("Restaurando as configurações anteriores ao tema...");
            var restore = await backups.RestoreAsync(record.BackupSessionId, ct);
            if (!restore.IsSuccess)
                return Result.Fail($"Falha ao restaurar o backup: {restore.Error}");
        }

        if (wingetIdsToRemove is { Count: > 0 })
        {
            var removable = await GetRemovableToolsAsync(themeId, ct);
            foreach (var id in wingetIdsToRemove)
            {
                var tool = removable.FirstOrDefault(t => t.WingetId == id);
                if (tool is null)
                {
                    progress?.Invoke($"⚠ '{id}' não foi instalado por este tema — ignorado.");
                    continue;
                }
                if (!tool.IsSafeToRemove)
                {
                    progress?.Invoke($"⚠ '{id}' ainda é usado por {tool.OtherThemesUsing} outro(s) tema(s) — mantido.");
                    continue;
                }

                progress?.Invoke($"Desinstalando {id} via WinGet...");
                var uninstall = await winGet.UninstallAsync(id, ct);
                if (!uninstall.IsSuccess)
                    progress?.Invoke($"⚠ {uninstall.Error}");
            }
        }

        await history.RemoveAsync(themeId, ct);
        await stateStore.RemoveThemeAsync(themeId, ct);

        logger?.LogInformation("Tema '{ThemeId}' ({ThemeName}) desinstalado.", themeId, record.ThemeName);
        progress?.Invoke($"Tema '{record.ThemeName}' removido.");
        return Result.Ok();
    }
}
