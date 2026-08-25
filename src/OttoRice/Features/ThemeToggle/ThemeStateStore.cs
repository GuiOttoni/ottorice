using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeToggle;

/// <summary>
/// Estado de UM tema instalado — uma entrada dentro de <see cref="InstalledThemes"/>
/// (%LOCALAPPDATA%\OttoRice\state.json). Guarda o wallpaper anterior por caminho E por
/// cópia local: com slideshow/Spotlight o caminho lido pelo Windows pode apontar para um
/// cache volátil.
/// </summary>
public sealed record ThemeState
{
    public string? ThemeId { get; init; }
    public string? ThemeName { get; init; }
    public bool IsEnabled { get; init; }

    /// <summary>URL/caminho que o usuário colou na aba Instalar — permite reaplicar o tema
    /// (rebaixar os arquivos e reaplicar) sem o usuário precisar colar de novo. Nulo para
    /// temas instalados antes desta versão.</summary>
    public string? SourceUrl { get; init; }

    public string? OriginalWallpaperPath { get; init; }
    public string? OriginalWallpaperCopy { get; init; }
    public string? ThemeWallpaperPath { get; init; }

    public string? GlazeWmConfigPath { get; init; }

    /// <summary>Ids do registry que este tema gerencia (glazewm, yasb, zebar...).</summary>
    public IReadOnlyList<string> ManagedApps { get; init; } = [];

    public DateTimeOffset InstalledAt { get; init; }

    public static readonly ThemeState Empty = new();
}

/// <summary>
/// Todos os temas instalados (RF-16/seção 12.3 do plano de evolução), com um ponteiro
/// separado indicando qual deles está ativo/ligado no momento. Substitui o antigo formato
/// de registro único (um <c>ThemeState</c> por <c>state.json</c>) — ver
/// <see cref="ThemeStateStore.ReadAsync"/> para a migração automática do formato antigo.
/// </summary>
public sealed record InstalledThemes
{
    public string? ActiveThemeId { get; init; }
    public IReadOnlyDictionary<string, ThemeState> Themes { get; init; } =
        new Dictionary<string, ThemeState>();

    public static readonly InstalledThemes Empty = new();

    public ThemeState? ActiveTheme =>
        ActiveThemeId is not null && Themes.TryGetValue(ActiveThemeId, out var state) ? state : null;

    public bool HasActiveTheme => ActiveTheme is not null;
}

public sealed class ThemeStateStore(string? rootOverride = null, ILogger<ThemeStateStore>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root = rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OttoRice");

    private string StatePath => Path.Combine(_root, "state.json");
    private string WallpaperBackupDir => Path.Combine(_root, "wallpaper-backup");

    /// <summary>
    /// Lê o estado de todos os temas instalados. Formato atual: objeto com "Themes"
    /// (dicionário por themeId) e "ActiveThemeId". Tolera dois casos legados, no mesmo
    /// espírito do tratamento já existente para JSON corrompido: (1) o formato antigo de
    /// registro único (um só ThemeState "solto", sem "Themes") é migrado em memória para
    /// uma entrada única — a próxima escrita já grava no formato novo; (2) JSON
    /// corrompido/ilegível cai em <see cref="InstalledThemes.Empty"/>, como antes.
    /// </summary>
    public async Task<InstalledThemes> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(StatePath))
            return InstalledThemes.Empty;

        string text;
        try
        {
            text = await File.ReadAllTextAsync(StatePath, ct);
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Falha ao ler '{StatePath}' — assumindo estado vazio.", StatePath);
            return InstalledThemes.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Themes", out _))
                return JsonSerializer.Deserialize<InstalledThemes>(text, JsonOptions) ?? InstalledThemes.Empty;

            return MigrateLegacySingleTheme(root);
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "state.json corrompido em '{StatePath}' — descartando e assumindo estado vazio.", StatePath);
            return InstalledThemes.Empty;
        }
    }

    private InstalledThemes MigrateLegacySingleTheme(JsonElement root)
    {
        var themeId = GetString(root, "ActiveThemeId");
        if (themeId is null)
            return InstalledThemes.Empty;

        var state = new ThemeState
        {
            ThemeId = themeId,
            ThemeName = GetString(root, "ActiveThemeName"),
            IsEnabled = root.TryGetProperty("IsEnabled", out var en) && en.ValueKind == JsonValueKind.True,
            SourceUrl = GetString(root, "SourceUrl"),
            OriginalWallpaperPath = GetString(root, "OriginalWallpaperPath"),
            OriginalWallpaperCopy = GetString(root, "OriginalWallpaperCopy"),
            ThemeWallpaperPath = GetString(root, "ThemeWallpaperPath"),
            GlazeWmConfigPath = GetString(root, "GlazeWmConfigPath"),
            ManagedApps = root.TryGetProperty("ManagedApps", out var apps) && apps.ValueKind == JsonValueKind.Array
                ? [.. apps.EnumerateArray().Select(a => a.GetString()).Where(s => s is not null).Select(s => s!)]
                : [],
            // Data de instalação não existia no formato antigo — melhor esforço.
            InstalledAt = DateTimeOffset.Now,
        };

        logger?.LogWarning(
            "state.json em formato antigo (registro único) detectado — migrando '{ThemeId}' para o novo formato multi-tema.",
            themeId);

        return new InstalledThemes
        {
            ActiveThemeId = themeId,
            Themes = new Dictionary<string, ThemeState> { [themeId] = state },
        };
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public Task WriteAsync(InstalledThemes state, CancellationToken ct = default) =>
        AtomicFileWriter.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(state, JsonOptions), ct);

    /// <summary>Grava/atualiza a entrada de um tema. <paramref name="makeActive"/> também move o
    /// ponteiro de tema ativo para ele (usado na instalação e na reaplicação).</summary>
    public async Task UpsertThemeAsync(ThemeState theme, bool makeActive = false, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(theme.ThemeId))
            throw new ArgumentException("ThemeState.ThemeId é obrigatório.", nameof(theme));

        var installed = await ReadAsync(ct);
        var themes = new Dictionary<string, ThemeState>(installed.Themes) { [theme.ThemeId] = theme };
        await WriteAsync(installed with
        {
            Themes = themes,
            ActiveThemeId = makeActive ? theme.ThemeId : installed.ActiveThemeId,
        }, ct);
    }

    /// <summary>Remove a entrada de um tema (desinstalação). Se ele era o ativo, o ponteiro é limpo.</summary>
    public async Task RemoveThemeAsync(string themeId, CancellationToken ct = default)
    {
        var installed = await ReadAsync(ct);
        if (!installed.Themes.ContainsKey(themeId))
            return;

        var themes = new Dictionary<string, ThemeState>(installed.Themes);
        themes.Remove(themeId);
        await WriteAsync(installed with
        {
            Themes = themes,
            ActiveThemeId = installed.ActiveThemeId == themeId ? null : installed.ActiveThemeId,
        }, ct);
    }

    /// <summary>Move o ponteiro de "tema ativo" sem alterar nenhuma entrada — usado ao trocar de tema.</summary>
    public async Task SetActiveThemeIdAsync(string? themeId, CancellationToken ct = default)
    {
        var installed = await ReadAsync(ct);
        await WriteAsync(installed with { ActiveThemeId = themeId }, ct);
    }

    public Task ClearAsync(CancellationToken ct = default) => WriteAsync(InstalledThemes.Empty, ct);

    /// <summary>Copia o wallpaper original para o cofre local (um arquivo por tema, para não
    /// colidir quando há mais de um tema instalado) e devolve o caminho da cópia.</summary>
    public async Task<string?> PreserveWallpaperAsync(
        string? originalPath, string themeId, CancellationToken ct = default)
    {
        if (originalPath is null || !File.Exists(originalPath))
            return null;

        var safeThemeId = string.Concat(themeId.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var copyPath = Path.Combine(WallpaperBackupDir, $"original-{safeThemeId}" + Path.GetExtension(originalPath));
        await AtomicFileWriter.CopyAsync(originalPath, copyPath, ct);
        return copyPath;
    }
}
