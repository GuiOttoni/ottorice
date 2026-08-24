using System;
using System.Collections.Generic;
using System.IO;
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

    public async Task AppendAsync(InstallRecord record, CancellationToken ct = default)
    {
        var records = new List<InstallRecord>(await ReadAllAsync(ct)) { record };
        await AtomicFileWriter.WriteAllTextAsync(
            _historyPath, JsonSerializer.Serialize(records, JsonOptions), ct, logger);
        logger?.LogInformation("Registro de instalação adicionado ao histórico: '{ThemeId}'.", record.ThemeId);
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
