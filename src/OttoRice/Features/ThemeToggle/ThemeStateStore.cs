using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeToggle;

/// <summary>
/// Estado do tema ativo (%LOCALAPPDATA%\OttoRice\state.json) — base do toggle (RF-15).
/// Guarda o wallpaper anterior por caminho E por cópia local: com slideshow/Spotlight o
/// caminho lido pelo Windows pode apontar para um cache volátil.
/// </summary>
public sealed record ThemeState
{
    public string? ActiveThemeId { get; init; }
    public string? ActiveThemeName { get; init; }
    public bool IsEnabled { get; init; }

    public string? OriginalWallpaperPath { get; init; }
    public string? OriginalWallpaperCopy { get; init; }
    public string? ThemeWallpaperPath { get; init; }

    public string? GlazeWmConfigPath { get; init; }

    /// <summary>Ids do registry que este tema gerencia (glazewm, yasb, zebar...).</summary>
    public IReadOnlyList<string> ManagedApps { get; init; } = [];

    public static readonly ThemeState Empty = new();

    public bool HasActiveTheme => ActiveThemeId is not null;
}

public sealed class ThemeStateStore(string? rootOverride = null, ILogger<ThemeStateStore>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root = rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OttoRice");

    private string StatePath => Path.Combine(_root, "state.json");
    private string WallpaperBackupDir => Path.Combine(_root, "wallpaper-backup");

    public async Task<ThemeState> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(StatePath))
            return ThemeState.Empty;
        try
        {
            return JsonSerializer.Deserialize<ThemeState>(await File.ReadAllTextAsync(StatePath, ct))
                   ?? ThemeState.Empty;
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "state.json corrompido em '{StatePath}' — descartando e assumindo estado vazio.", StatePath);
            return ThemeState.Empty;
        }
    }

    public Task WriteAsync(ThemeState state, CancellationToken ct = default) =>
        AtomicFileWriter.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(state, JsonOptions), ct);

    public Task ClearAsync(CancellationToken ct = default) => WriteAsync(ThemeState.Empty, ct);

    /// <summary>Copia o wallpaper original para o cofre local e devolve o caminho da cópia.</summary>
    public async Task<string?> PreserveWallpaperAsync(string? originalPath, CancellationToken ct = default)
    {
        if (originalPath is null || !File.Exists(originalPath))
            return null;

        var copyPath = Path.Combine(WallpaperBackupDir, "original" + Path.GetExtension(originalPath));
        await AtomicFileWriter.CopyAsync(originalPath, copyPath, ct);
        return copyPath;
    }
}
