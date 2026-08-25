using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.BackupRestore;

public sealed record InstallRecord(
    string ThemeId,
    string ThemeName,
    string BackupSessionId,
    DateTimeOffset InstalledAt,
    IReadOnlyList<string> WingetIdsInstalled);

/// <summary>
/// Histórico de temas aplicados (%LOCALAPPDATA%\OttoRice\history.json) — RF-09.
/// É a base para desinstalar/alternar temas: cada registro aponta a sessão de backup
/// que restaura o estado pré-tema e quais pacotes o tema trouxe via WinGet.
/// </summary>
public sealed class InstallHistoryStore(string? rootOverride = null, ILogger<InstallHistoryStore>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _historyPath = Path.Combine(
        rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OttoRice"),
        "history.json");

    /// <summary>Upsert por <see cref="InstallRecord.ThemeId"/>: instalar o mesmo tema de novo
    /// (sem desinstalar antes) substitui o registro anterior em vez de duplicá-lo. Antes desta
    /// correção, reinstalar produzia dois registros do mesmo tema e
    /// <see cref="ReadAllAsync"/>/<c>FirstOrDefault(r =&gt; r.ThemeId == themeId)</c> em
    /// <see cref="OttoRice.Features.ThemeUninstall.UninstallService"/> só enxergava o primeiro —
    /// achado registrado na seção 12.5 do plano de evolução (docs/ottorice.md).</summary>
    public async Task AppendAsync(InstallRecord record, CancellationToken ct = default)
    {
        var records = (await ReadAllAsync(ct)).Where(r => r.ThemeId != record.ThemeId).ToList();
        records.Add(record);
        await AtomicFileWriter.WriteAllTextAsync(
            _historyPath, JsonSerializer.Serialize(records, JsonOptions), ct, logger);
        logger?.LogInformation("Registro de instalação gravado no histórico: '{ThemeId}'.", record.ThemeId);
    }

    public async Task RemoveAsync(string themeId, CancellationToken ct = default)
    {
        var remaining = new List<InstallRecord>();
        foreach (var record in await ReadAllAsync(ct))
        {
            if (record.ThemeId != themeId)
                remaining.Add(record);
        }
        await AtomicFileWriter.WriteAllTextAsync(
            _historyPath, JsonSerializer.Serialize(remaining, JsonOptions), ct, logger);
        logger?.LogInformation("Registro de instalação removido do histórico: '{ThemeId}'.", themeId);
    }

    public async Task<IReadOnlyList<InstallRecord>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_historyPath))
            return [];
        return JsonSerializer.Deserialize<List<InstallRecord>>(
            await File.ReadAllTextAsync(_historyPath, ct)) ?? [];
    }
}
