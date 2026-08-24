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
    private const string GlazePath = @"C:\Program Files\glzr.io\GlazeWM\cli\glazewm.exe";
    private const string YasbPath = @"C:\Program Files\YASB\yasbc.exe";

    private static IExecutableResolver Resolver()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve("glazewm").Returns(GlazePath);
        resolver.Resolve("yasbc").Returns(YasbPath);
        return resolver;
    }

    [Fact]
    public async Task GlazeWm_reload_uses_whitelisted_command_on_resolved_path()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(GlazePath, "command wm-reload-config", Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(0, "", ""));

        var result = await new AppReloader(runner, Resolver()).ReloadAsync(ReloadAction.GlazeWm);

        Assert.True(result.IsSuccess);
        runner.DidNotReceiveWithAnyArgs().StartDetached(default!, default!);
    }

    [Fact]
    public async Task Starts_glazewm_with_start_argument_when_not_running()
    {
        // Regressão do dogfooding: iniciar sem "start" não sobe o WM.
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(GlazePath, "command wm-reload-config", Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(1, "", "not running"));

        var result = await new AppReloader(runner, Resolver()).ReloadAsync(ReloadAction.GlazeWm);

        Assert.True(result.IsSuccess);
        runner.Received(1).StartDetached(GlazePath, "start");
    }

    [Fact]
    public async Task Starts_yasb_detached_when_reload_fails()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(YasbPath, "reload", Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(1, "", "not running"));

        var result = await new AppReloader(runner, Resolver()).ReloadAsync(ReloadAction.Yasb);

        Assert.True(result.IsSuccess);
        runner.Received(1).StartDetached(YasbPath, "start --silent");
    }

    [Fact]
    public async Task Reports_clearly_when_executable_cannot_be_found()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns((string?)null);

        var result = await new AppReloader(Substitute.For<IProcessRunner>(), resolver)
            .ReloadAsync(ReloadAction.GlazeWm);

        Assert.False(result.IsSuccess);
        Assert.Contains("não encontrado", result.Error);
    }
}

public class ConfigureWindhawkModsStepTests
{
    private static InstallContext Context(params RiceTarget[] targets)
    {
        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = Path.GetTempPath(),
        };
        foreach (var target in targets)
            context.Operations.Add(new FileOperation(target, "", ""));
        return context;
    }

    private static RiceTarget ModTarget(string app = "windows-11-taskbar-styler", string? theme = "FrostyGlass") => new()
    {
        App = app,
        Action = "configure_mod",
        Settings = theme is null ? new() : new() { ["theme"] = theme },
    };

    [Fact]
    public async Task No_mod_targets_is_a_noop()
    {
        var runner = Substitute.For<IProcessRunner>();
        var result = await new ConfigureWindhawkModsStep(Substitute.For<IExecutableResolver>(), runner)
            .ExecuteAsync(Context(new RiceTarget { App = "glazewm", Action = "override" }));

        Assert.True(result.IsSuccess);
        await runner.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default!);
    }

    [Fact]
    public async Task Missing_windhawk_cli_is_non_fatal_and_skips()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve("windhawk-cli").Returns((string?)null);
        var runner = Substitute.For<IProcessRunner>();

        var context = Context(ModTarget());
        var result = await new ConfigureWindhawkModsStep(resolver, runner).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        await runner.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default!);
    }

    private static InstallContext ContextWithLog(List<string> log, params RiceTarget[] targets)
    {
        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = Path.GetTempPath(),
            Progress = log.Add,
        };
        foreach (var target in targets)
            context.Operations.Add(new FileOperation(target, "", ""));
        return context;
    }

    [Fact]
    public async Task Success_runs_one_elevated_batch_and_starts_windhawk_if_not_running()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve("windhawk-cli").Returns(@"C:\Program Files\Windhawk\windhawk-cli.exe");
        resolver.Resolve("windhawk").Returns(@"C:\Program Files\Windhawk\windhawk.exe");
        var runner = Substitute.For<IProcessRunner>();
        runner.RunElevatedAsync("cmd.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<int?>(0));
        runner.FindProcessIds("windhawk").Returns([]);

        var log = new List<string>();
        var context = ContextWithLog(log, ModTarget());

        var result = await new ConfigureWindhawkModsStep(resolver, runner).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        await runner.Received(1).RunElevatedAsync(
            "cmd.exe", Arg.Is<string>(a => a.Contains("/c") && a.Contains(".cmd")), Arg.Any<CancellationToken>());
        Assert.Contains(log, l => l.Contains("configurados"));
        runner.Received(1).StartDetached(@"C:\Program Files\Windhawk\windhawk.exe", Arg.Any<string>());
    }

    [Fact]
    public async Task Success_does_not_start_windhawk_again_if_already_running()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve("windhawk-cli").Returns(@"C:\Program Files\Windhawk\windhawk-cli.exe");
        var runner = Substitute.For<IProcessRunner>();
        runner.RunElevatedAsync("cmd.exe", Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<int?>(0));
        runner.FindProcessIds("windhawk").Returns([1234]);

        var context = ContextWithLog([], ModTarget());
        var result = await new ConfigureWindhawkModsStep(resolver, runner).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        runner.DidNotReceiveWithAnyArgs().StartDetached(default!, default!);
    }

    [Fact]
    public async Task Uac_cancelled_is_non_fatal_and_reports_warning()
    {
        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve("windhawk-cli").Returns(@"C:\Program Files\Windhawk\windhawk-cli.exe");
        var runner = Substitute.For<IProcessRunner>();
        runner.RunElevatedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<int?>(null));

        var log = new List<string>();
        var context = ContextWithLog(log, ModTarget());

        var result = await new ConfigureWindhawkModsStep(resolver, runner).ExecuteAsync(context);

        Assert.True(result.IsSuccess);
        Assert.Contains(log, l => l.Contains("UAC"));
    }

    [Theory]
    [InlineData("ok\" & del /f /q C:\\* & \"")]
    [InlineData("has spaces\r\ninjected")]
    public async Task Unsafe_settings_value_aborts_before_touching_the_cli(string unsafeValue)
    {
        var resolver = Substitute.For<IExecutableResolver>();
        var runner = Substitute.For<IProcessRunner>();

        var context = Context(ModTarget(theme: unsafeValue));
        var result = await new ConfigureWindhawkModsStep(resolver, runner).ExecuteAsync(context);

        Assert.True(result.IsSuccess); // melhor esforço: não derruba o pipeline
        resolver.DidNotReceiveWithAnyArgs().Resolve(default!);
        await runner.DidNotReceiveWithAnyArgs().RunElevatedAsync(default!, default!);
    }
}
