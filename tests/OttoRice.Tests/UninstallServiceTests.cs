using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeToggle;
using OttoRice.Features.ThemeUninstall;

namespace OttoRice.Tests;

public class UninstallServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-uninstall").FullName;
    private readonly InstallHistoryStore _history;
    private readonly BackupSessionStore _backups;
    private readonly ThemeStateStore _stateStore;
    private readonly IWinGetClient _winGet = Substitute.For<IWinGetClient>();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly UninstallService _uninstall;

    public UninstallServiceTests()
    {
        _history = new InstallHistoryStore(_dir);
        _backups = new BackupSessionStore(Path.Combine(_dir, "backups"));
        _stateStore = new ThemeStateStore(_dir);
        _winGet.UninstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new ProcessResult(0, "", ""));
        _runner.FindProcessIds(Arg.Any<string>()).Returns([]);

        var resolver = Substitute.For<IExecutableResolver>();
        resolver.Resolve(Arg.Any<string>()).Returns(call => call.Arg<string>());

        _uninstall = new UninstallService(
            _history, _backups, _stateStore,
            new ThemeToggleService(_runner, _wallpaper, _stateStore, resolver, Substitute.For<ITaskbarService>()),
            _winGet);
    }

    private Task SeedThemeAsync(string themeId, string backupSessionId, params string[] wingetIds) =>
        _history.AppendAsync(new InstallRecord(
            themeId, $"Tema {themeId}", backupSessionId, DateTimeOffset.Now, wingetIds));

    [Fact]
    public async Task Refcount_marks_shared_tools_as_unsafe_to_remove()
    {
        await SeedThemeAsync("tema-a", "", "glazewm.glazewm", "AmN.yasb");
        await SeedThemeAsync("tema-b", "", "glazewm.glazewm");

        var tools = await _uninstall.GetRemovableToolsAsync("tema-a");

        var glaze = tools.Single(t => t.WingetId == "glazewm.glazewm");
        var yasb = tools.Single(t => t.WingetId == "AmN.yasb");
        Assert.False(glaze.IsSafeToRemove);
        Assert.Equal(1, glaze.OtherThemesUsing);
        Assert.True(yasb.IsSafeToRemove);
    }

    [Fact]
    public async Task Uninstall_restores_backup_and_removes_history_record()
    {
        var config = Path.Combine(_dir, "config.yaml");
        File.WriteAllText(config, "original");
        var session = await _backups.CreateSessionAsync("tema-a", [config]);
        await SeedThemeAsync("tema-a", session.Id);
        File.WriteAllText(config, "aplicado pelo tema");

        var result = await _uninstall.UninstallAsync("tema-a");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("original", File.ReadAllText(config));
        Assert.Empty(await _history.ReadAllAsync());
    }

    [Fact]
    public async Task Shared_tool_is_never_uninstalled_even_if_requested()
    {
        await SeedThemeAsync("tema-a", "", "glazewm.glazewm");
        await SeedThemeAsync("tema-b", "", "glazewm.glazewm");

        var result = await _uninstall.UninstallAsync("tema-a", ["glazewm.glazewm"]);

        Assert.True(result.IsSuccess, result.Error);
        await _winGet.DidNotReceive().UninstallAsync("glazewm.glazewm", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unshared_tool_is_uninstalled_only_when_selected()
    {
        await SeedThemeAsync("tema-a", "", "AmN.yasb", "glazewm.glazewm");

        await _uninstall.UninstallAsync("tema-a", ["AmN.yasb"]);

        await _winGet.Received(1).UninstallAsync("AmN.yasb", Arg.Any<CancellationToken>());
        await _winGet.DidNotReceive().UninstallAsync("glazewm.glazewm", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tools_are_kept_by_default_when_nothing_is_selected()
    {
        await SeedThemeAsync("tema-a", "", "AmN.yasb");

        await _uninstall.UninstallAsync("tema-a");

        await _winGet.DidNotReceiveWithAnyArgs().UninstallAsync(default!, default);
    }

    [Fact]
    public async Task Active_enabled_theme_is_turned_off_before_removal()
    {
        await SeedThemeAsync("tema-a", "");
        await _stateStore.WriteAsync(new ThemeState
        {
            ActiveThemeId = "tema-a",
            ActiveThemeName = "Tema A",
            IsEnabled = true,
            ManagedApps = ["glazewm"],
        });

        var result = await _uninstall.UninstallAsync("tema-a");

        Assert.True(result.IsSuccess, result.Error);
        await _runner.Received(1).RunAsync("glazewm", "command wm-exit", Arg.Any<CancellationToken>());
        Assert.False((await _stateStore.ReadAsync()).HasActiveTheme);
    }

    [Fact]
    public async Task Unknown_theme_fails()
    {
        var result = await _uninstall.UninstallAsync("nao-existe");
        Assert.False(result.IsSuccess);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
