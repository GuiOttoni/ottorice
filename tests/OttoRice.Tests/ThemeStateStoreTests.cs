using System.Text.Json;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Tests;

/// <summary>
/// ThemeStateStore/InstalledThemes (seção 12.3 do plano de evolução): o app passou de
/// rastrear um único ThemeState ativo para uma coleção de temas instalados, com um ponteiro
/// separado para qual está ativo. Cobre o modelo novo e a migração automática do formato
/// antigo (registro único, sem "Themes") — o mesmo espírito de tolerância a state.json
/// corrompido que o ReadAsync já tinha antes desta mudança.
/// </summary>
public class ThemeStateStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-statestore").FullName;
    private readonly ThemeStateStore _store;

    public ThemeStateStoreTests() => _store = new ThemeStateStore(_dir);

    private string StatePath => Path.Combine(_dir, "state.json");

    [Fact]
    public async Task Missing_file_returns_empty()
    {
        var installed = await _store.ReadAsync();
        Assert.False(installed.HasActiveTheme);
        Assert.Empty(installed.Themes);
    }

    [Fact]
    public async Task Corrupted_json_falls_back_to_empty_instead_of_throwing()
    {
        File.WriteAllText(StatePath, "{ not json");

        var installed = await _store.ReadAsync();

        Assert.False(installed.HasActiveTheme);
        Assert.Empty(installed.Themes);
    }

    [Fact]
    public async Task Upsert_adds_a_theme_without_touching_others()
    {
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "a", ThemeName = "A" }, makeActive: true);
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "b", ThemeName = "B" });

        var installed = await _store.ReadAsync();

        Assert.Equal(2, installed.Themes.Count);
        Assert.Equal("a", installed.ActiveThemeId);
        Assert.Equal("A", installed.Themes["a"].ThemeName);
        Assert.Equal("B", installed.Themes["b"].ThemeName);
    }

    [Fact]
    public async Task RemoveTheme_clears_active_pointer_only_if_it_was_the_removed_theme()
    {
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "a", ThemeName = "A" });
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "b", ThemeName = "B" }, makeActive: true);

        await _store.RemoveThemeAsync("a");
        var afterRemovingInactive = await _store.ReadAsync();
        Assert.Equal("b", afterRemovingInactive.ActiveThemeId);
        Assert.False(afterRemovingInactive.Themes.ContainsKey("a"));

        await _store.RemoveThemeAsync("b");
        var afterRemovingActive = await _store.ReadAsync();
        Assert.Null(afterRemovingActive.ActiveThemeId);
        Assert.Empty(afterRemovingActive.Themes);
    }

    [Fact]
    public async Task SetActiveThemeId_moves_the_pointer_without_altering_entries()
    {
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "a", ThemeName = "A", IsEnabled = true }, makeActive: true);
        await _store.UpsertThemeAsync(new ThemeState { ThemeId = "b", ThemeName = "B" });

        await _store.SetActiveThemeIdAsync("b");

        var installed = await _store.ReadAsync();
        Assert.Equal("b", installed.ActiveThemeId);
        Assert.True(installed.Themes["a"].IsEnabled); // entrada não mexida
    }

    [Fact]
    public async Task Legacy_single_theme_state_json_is_migrated_on_read()
    {
        // Formato antigo (pré seção 12.3): um único ThemeState "solto" no arquivo, sem "Themes".
        var legacyJson = JsonSerializer.Serialize(new
        {
            ActiveThemeId = "tema-legado",
            ActiveThemeName = "Tema Legado",
            IsEnabled = true,
            SourceUrl = "https://github.com/o/r",
            OriginalWallpaperPath = @"C:\old\wall.jpg",
            OriginalWallpaperCopy = (string?)null,
            ThemeWallpaperPath = @"C:\tema\wall.png",
            GlazeWmConfigPath = @"C:\tema\glazewm.yaml",
            ManagedApps = new[] { "glazewm", "yasb" },
        });
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(StatePath, legacyJson);

        var installed = await _store.ReadAsync();

        Assert.Equal("tema-legado", installed.ActiveThemeId);
        var state = installed.Themes["tema-legado"];
        Assert.Equal("Tema Legado", state.ThemeName);
        Assert.True(state.IsEnabled);
        Assert.Equal("https://github.com/o/r", state.SourceUrl);
        Assert.Equal(@"C:\tema\glazewm.yaml", state.GlazeWmConfigPath);
        Assert.Contains("glazewm", state.ManagedApps);
        Assert.Contains("yasb", state.ManagedApps);
    }

    [Fact]
    public async Task Legacy_state_with_no_active_theme_migrates_to_empty()
    {
        // Legado sem tema nenhum (usuário nunca instalou) — ActiveThemeId nulo/ausente.
        await File.WriteAllTextAsync(StatePath, "{}");

        var installed = await _store.ReadAsync();

        Assert.False(installed.HasActiveTheme);
        Assert.Empty(installed.Themes);
    }

    [Fact]
    public async Task Migrated_legacy_state_is_persisted_in_the_new_format_on_next_write()
    {
        var legacyJson = JsonSerializer.Serialize(new { ActiveThemeId = "t", ActiveThemeName = "T", IsEnabled = false });
        await File.WriteAllTextAsync(StatePath, legacyJson);

        var installed = await _store.ReadAsync();
        await _store.WriteAsync(installed);

        var rawAfterWrite = await File.ReadAllTextAsync(StatePath);
        Assert.Contains("\"Themes\"", rawAfterWrite);

        // Uma segunda leitura pega o caminho normal (não o de migração) e continua correta.
        var rereadInstalled = await _store.ReadAsync();
        Assert.Equal("t", rereadInstalled.ActiveThemeId);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
