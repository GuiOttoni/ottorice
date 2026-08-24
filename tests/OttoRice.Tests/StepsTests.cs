using NSubstitute;
using OttoRice.AppRegistry;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Tests;

public class DependencyStepTests
{
    private static InstallContext Context(params string[] wingetIds) => new()
    {
        Manifest = new RiceManifest
        {
            ThemeId = "t",
            Name = "T",
            Dependencies = [.. wingetIds.Select(id => new RiceDependency { WingetId = id })],
        },
        ThemeDirectory = Path.GetTempPath(),
    };

    [Fact]
    public async Task Installs_only_missing_dependencies_and_records_them()
    {
        var winget = Substitute.For<IWinGetClient>();
        winget.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        winget.IsInstalledAsync("ja.instalado", Arg.Any<CancellationToken>()).Returns(true);
        winget.IsInstalledAsync("falta.este", Arg.Any<CancellationToken>()).Returns(false);
        winget.InstallAsync("falta.este", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var context = Context("ja.instalado", "falta.este");
        var result = await new DependencyStep(winget).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        await winget.DidNotReceive().InstallAsync("ja.instalado", Arg.Any<CancellationToken>());
        Assert.Equal(["falta.este"], context.WingetIdsInstalled);
    }

    [Fact]
    public async Task Fails_fast_when_winget_is_unavailable()
    {
        var winget = Substitute.For<IWinGetClient>();
        winget.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await new DependencyStep(winget).ExecuteAsync(Context("qualquer.pacote"));

        Assert.False(result.IsSuccess);
        Assert.Contains("WinGet", result.Error);
    }
}

public class BackupStepTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-backupstep").FullName;

    [Fact]
    public async Task Compensate_restores_files_and_previous_wallpaper()
    {
        var config = Path.Combine(_dir, "config.yaml");
        File.WriteAllText(config, "original");

        var wallpaper = Substitute.For<IWallpaperService>();
        wallpaper.GetCurrentPath().Returns(@"C:\old\wall.jpg");

        var store = new BackupSessionStore(Path.Combine(_dir, "backups"));
        var step = new BackupStep(store, wallpaper);

        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = _dir,
        };
        context.Operations.Add(new FileOperation(
            new RiceTarget { App = "glazewm", Action = "override", Source = "s" }, "src", config));
        context.Operations.Add(new FileOperation(
            new RiceTarget { App = "wallpaper", Action = "set", Source = "w" }, "wall.png", ""));

        Assert.True((await step.ExecuteAsync(context)).IsSuccess);
        File.WriteAllText(config, "tema"); // simula apply

        await step.CompensateAsync(context);

        Assert.Equal("original", File.ReadAllText(config));
        wallpaper.Received(1).Set(@"C:\old\wall.jpg");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}

public class ReloadStepTests
{
    [Fact]
    public async Task Reload_failure_does_not_fail_pipeline()
    {
        var reloader = Substitute.For<IAppReloader>();
        reloader.ReloadAsync(Arg.Any<ReloadAction>(), Arg.Any<CancellationToken>())
                .Returns(Result.Fail("não encontrado"));

        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = Path.GetTempPath(),
        };
        context.Operations.Add(new FileOperation(
            new RiceTarget { App = "glazewm", Action = "override", Source = "s" }, "a", "b"));

        var result = await new ReloadStep(reloader).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        await reloader.Received(1).ReloadAsync(ReloadAction.GlazeWm, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_apps_reload_once_and_wallpaper_is_skipped()
    {
        var reloader = Substitute.For<IAppReloader>();
        reloader.ReloadAsync(Arg.Any<ReloadAction>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = Path.GetTempPath(),
        };
        var glaze = new RiceTarget { App = "glazewm", Action = "override", Source = "s" };
        context.Operations.Add(new FileOperation(glaze, "a", "b"));
        context.Operations.Add(new FileOperation(glaze, "c", "d"));
        context.Operations.Add(new FileOperation(
            new RiceTarget { App = "wallpaper", Action = "set", Source = "w" }, "w", ""));

        await new ReloadStep(reloader).ExecuteAsync(context);

        await reloader.Received(1).ReloadAsync(ReloadAction.GlazeWm, Arg.Any<CancellationToken>());
        await reloader.DidNotReceive().ReloadAsync(ReloadAction.Wallpaper, Arg.Any<CancellationToken>());
    }
}

public class AppReloaderTests
{
    [Fact]
    public async Task GlazeWm_reload_uses_whitelisted_command()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync("glazewm", "command wm-reload-config", Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(0, "", ""));

        var result = await new AppReloader(runner).ReloadAsync(ReloadAction.GlazeWm);

        Assert.True(result.IsSuccess);
        runner.DidNotReceiveWithAnyArgs().StartDetached(default!, default!);
    }

    [Fact]
    public async Task Starts_detached_when_reload_command_fails()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync("yasbc", "reload", Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(1, "", "not running"));

        var result = await new AppReloader(runner).ReloadAsync(ReloadAction.Yasb);

        Assert.True(result.IsSuccess);
        runner.Received(1).StartDetached("yasbc", "start");
    }
}
