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

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
