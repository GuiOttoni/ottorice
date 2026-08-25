using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Tests;

/// <summary>
/// ReapplyThemeService (seção 12.2/12.3 do plano de evolução): reaplica um tema já instalado
/// (qualquer um, não só o ativo) rebaixando a origem salva e rodando uma pipeline reduzida
/// (sem Dependência/Backup).
/// </summary>
public class ReapplyThemeServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-reapply").FullName;
    private readonly IThemeFetcher _fetcher = Substitute.For<IThemeFetcher>();
    private readonly ThemeStateStore _store;

    public ReapplyThemeServiceTests() => _store = new ThemeStateStore(_dir);

    private sealed class RecordingStep(string name, List<string> log) : IInstallStep
    {
        public string Name => name;

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            log.Add(Name);
            context.Operations.Add(new FileOperation(
                new RiceTarget { App = "glazewm", Action = "override", Source = "s" },
                "src", "dest-config.yaml"));
            return Task.FromResult(Result.Ok());
        }
    }

    private static FetchedTheme Fetched(string themeDir = "irrelevante") =>
        new(themeDir, new RiceManifest { ThemeId = "t", Name = "T" });

    [Fact]
    public async Task Fails_when_the_theme_is_not_installed()
    {
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.ReapplyAsync("t");

        Assert.False(result.IsSuccess);
        Assert.Contains("não está instalado", result.Error);
        await _fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!);
    }

    [Fact]
    public async Task Fails_with_actionable_message_when_theme_has_no_saved_source_url()
    {
        await _store.UpsertThemeAsync(
            new ThemeState { ThemeId = "t", ThemeName = "T", IsEnabled = true }, makeActive: true);
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.ReapplyAsync("t");

        Assert.False(result.IsSuccess);
        Assert.Contains("origem salva", result.Error);
    }

    [Fact]
    public async Task Propagates_fetch_failure()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "t", ThemeName = "T", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Fail("sem rede"));
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.ReapplyAsync("t");

        Assert.False(result.IsSuccess);
        Assert.Contains("sem rede", result.Error);
    }

    [Fact]
    public async Task Does_not_reapply_when_pipeline_fails_and_does_not_touch_state()
    {
        var original = new ThemeState
        {
            ThemeId = "t", ThemeName = "T", IsEnabled = true,
            SourceUrl = "https://github.com/o/r", GlazeWmConfigPath = @"C:\old\config.yaml",
        };
        await _store.UpsertThemeAsync(original, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(Fetched()));
        var failingPipeline = new InstallPipeline([new AlwaysFailStep()]);
        var service = new ReapplyThemeService(_fetcher, failingPipeline, _store);

        var result = await service.ReapplyAsync("t");

        Assert.False(result.IsSuccess);
        var saved = (await _store.ReadAsync()).Themes["t"];
        Assert.Equal(original.GlazeWmConfigPath, saved.GlazeWmConfigPath);
        Assert.Equal(original.SourceUrl, saved.SourceUrl);
        Assert.Empty(saved.ManagedApps);
    }

    private sealed class AlwaysFailStep : IInstallStep
    {
        public string Name => "falha";
        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default) =>
            Task.FromResult(Result.Fail("boom"));
    }

    [Fact]
    public async Task Success_runs_pipeline_and_updates_derived_paths_preserving_the_rest()
    {
        var original = new ThemeState
        {
            ThemeId = "t",
            ThemeName = "T",
            IsEnabled = true,
            SourceUrl = "https://github.com/o/r",
            OriginalWallpaperPath = @"C:\old\wall.jpg",
            GlazeWmConfigPath = @"C:\stale\config.yaml",
        };
        await _store.UpsertThemeAsync(original, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(Fetched()));

        var log = new List<string>();
        var pipeline = new InstallPipeline([new RecordingStep("Aplicação", log)]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ReapplyAsync("t");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["Aplicação"], log);

        var saved = (await _store.ReadAsync()).Themes["t"];
        // Origem/wallpaper anterior preservados — não são responsabilidade do reaply.
        Assert.Equal(original.SourceUrl, saved.SourceUrl);
        Assert.Equal(original.OriginalWallpaperPath, saved.OriginalWallpaperPath);
        // Caminho derivado atualizado pela operação que a pipeline reduzida planejou.
        Assert.Equal("dest-config.yaml", saved.GlazeWmConfigPath);
        Assert.Contains("glazewm", saved.ManagedApps);
    }

    /// <summary>Toggle por componente na reaplicação: só o(s) target(s) selecionado(s) chega(m)
    /// ao InstallContext (via <see cref="InstallContext.SelectedTargetIndexes"/>).</summary>
    [Fact]
    public async Task Selected_target_indexes_flow_through_to_the_pipeline_context()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "t", ThemeName = "T", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(Fetched()));

        InstallContext? captured = null;
        var capturingStep = new CapturingStep(ctx => captured = ctx);
        var pipeline = new InstallPipeline([capturingStep]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ReapplyAsync("t", selectedTargetIndexes: new HashSet<int> { 0 });

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(captured);
        Assert.Equal(new HashSet<int> { 0 }, captured!.SelectedTargetIndexes);
    }

    private sealed class CapturingStep(Action<InstallContext> capture) : IInstallStep
    {
        public string Name => "captura";

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            capture(context);
            return Task.FromResult(Result.Ok());
        }
    }

    /// <summary>FetchTargetsAsync popula a UI de seleção antes de reaplicar, sem aplicar nada.</summary>
    [Fact]
    public async Task FetchTargetsAsync_returns_the_manifest_targets_without_applying_anything()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "t", ThemeName = "T", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        var manifest = new RiceManifest
        {
            ThemeId = "t",
            Name = "T",
            Targets =
            [
                new RiceTarget { App = "glazewm", Action = "override", Source = "s1" },
                new RiceTarget { App = "wallpaper", Action = "set", Source = "s2" },
            ],
        };
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", manifest)));
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.FetchTargetsAsync("t");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("glazewm", result.Value[0].App);
        Assert.Equal("wallpaper", result.Value[1].App);
    }

    [Fact]
    public async Task Can_reapply_an_installed_theme_that_is_not_the_active_one()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "ativo", ThemeName = "Ativo", IsEnabled = true, SourceUrl = "https://github.com/o/ativo",
        }, makeActive: true);
        var inactive = new ThemeState
        {
            ThemeId = "inativo", ThemeName = "Inativo", IsEnabled = false, SourceUrl = "https://github.com/o/inativo",
        };
        await _store.UpsertThemeAsync(inactive);

        _fetcher.FetchAsync("https://github.com/o/inativo", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(Fetched()));
        var pipeline = new InstallPipeline([]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ReapplyAsync("inativo");

        Assert.True(result.IsSuccess, result.Error);
        var installed = await _store.ReadAsync();
        Assert.Equal("ativo", installed.ActiveThemeId); // reaplicar não muda o tema ativo
        Assert.False(installed.Themes["inativo"].IsEnabled);
    }

    // ── paletas (seção 13 da doc "OttoRice") ───────────────────────────────

    private static RiceManifest ManifestWithPalettes() => new()
    {
        ThemeId = "catppuccin",
        Name = "Catppuccin",
        Targets = [new RiceTarget { App = "glazewm", Action = "override", Source = "configs/glazewm/config.yaml" }],
        Palettes =
        [
            new RicePalette { Id = "latte", Name = "Catppuccin Latte", SourceOverride = "palettes/latte" },
            new RicePalette { Id = "frappe", Name = "Catppuccin Frappé", SourceOverride = "palettes/frappe" },
        ],
    };

    [Fact]
    public async Task FetchPalettesAsync_returns_the_manifest_palettes()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "catppuccin", ThemeName = "Catppuccin", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", ManifestWithPalettes())));
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.FetchPalettesAsync("catppuccin");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value, p => p.Id == "latte");
    }

    [Fact]
    public async Task FetchPalettesAsync_on_theme_without_palettes_returns_empty_not_a_failure()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "t", ThemeName = "T", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(Fetched()));
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.FetchPalettesAsync("t");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ApplyPaletteAsync_fails_when_the_palette_id_does_not_exist_in_the_manifest()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "catppuccin", ThemeName = "Catppuccin", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", ManifestWithPalettes())));
        var service = new ReapplyThemeService(_fetcher, new InstallPipeline([]), _store);

        var result = await service.ApplyPaletteAsync("catppuccin", "nao-existe");

        Assert.False(result.IsSuccess);
        Assert.Contains("não existe mais", result.Error);
    }

    [Fact]
    public async Task ApplyPaletteAsync_sets_the_palette_id_on_the_pipeline_context_and_persists_it()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "catppuccin", ThemeName = "Catppuccin", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", ManifestWithPalettes())));

        InstallContext? captured = null;
        var pipeline = new InstallPipeline([new CapturingStep(ctx => captured = ctx)]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ApplyPaletteAsync("catppuccin", "latte");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("latte", captured!.PaletteId);
        // Troca de paleta sempre reaplica todos os targets — nunca herda um toggle antigo.
        Assert.Null(captured.SelectedTargetIndexes);

        var saved = (await _store.ReadAsync()).Themes["catppuccin"];
        Assert.Equal("latte", saved.ActivePaletteId);
    }

    [Fact]
    public async Task ApplyPaletteAsync_with_null_id_switches_back_to_the_default_palette()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "catppuccin", ThemeName = "Catppuccin", IsEnabled = true,
            SourceUrl = "https://github.com/o/r", ActivePaletteId = "latte",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", ManifestWithPalettes())));

        InstallContext? captured = null;
        var pipeline = new InstallPipeline([new CapturingStep(ctx => captured = ctx)]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ApplyPaletteAsync("catppuccin", null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(captured!.PaletteId);

        var saved = (await _store.ReadAsync()).Themes["catppuccin"];
        Assert.Null(saved.ActivePaletteId);
    }

    /// <summary>Reaplicação "normal" (botão REAPLICAR) não reseta a paleta ativa — ela flui
    /// pro InstallContext pra o PlanStep continuar resolvendo a partir do diretório certo.</summary>
    [Fact]
    public async Task ReapplyAsync_preserves_the_currently_active_palette()
    {
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "catppuccin", ThemeName = "Catppuccin", IsEnabled = true,
            SourceUrl = "https://github.com/o/r", ActivePaletteId = "frappe",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", ManifestWithPalettes())));

        InstallContext? captured = null;
        var pipeline = new InstallPipeline([new CapturingStep(ctx => captured = ctx)]);
        var service = new ReapplyThemeService(_fetcher, pipeline, _store);

        var result = await service.ReapplyAsync("catppuccin");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("frappe", captured!.PaletteId);

        var saved = (await _store.ReadAsync()).Themes["catppuccin"];
        Assert.Equal("frappe", saved.ActivePaletteId); // continua igual, não foi resetada
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
