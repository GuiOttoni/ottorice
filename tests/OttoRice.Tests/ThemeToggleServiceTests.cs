using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Tests;

public class ThemeToggleServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-toggle").FullName;
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly ThemeStateStore _store;
    private readonly ThemeToggleService _toggle;

    private readonly IExecutableResolver _resolver = Substitute.For<IExecutableResolver>();

    public ThemeToggleServiceTests()
    {
        _store = new ThemeStateStore(_dir);
        // Resolver "identidade": os testes continuam asserindo pelo nome do comando.
        _resolver.Resolve(Arg.Any<string>()).Returns(call => call.Arg<string>());
        _toggle = new ThemeToggleService(_runner, _wallpaper, _store, _resolver);
        _runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(new ProcessResult(0, "", ""));
        _runner.FindProcessIds(Arg.Any<string>()).Returns([]);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "img");
        return path;
    }

    private async Task<ThemeState> SeedEnabledThemeAsync(string? originalWallpaper = null, string themeId = "tema-x")
    {
        var state = new ThemeState
        {
            ThemeId = themeId,
            ThemeName = "Tema X",
            IsEnabled = true,
            OriginalWallpaperCopy = originalWallpaper,
            ThemeWallpaperPath = CreateFile($"theme-wall-{themeId}.png"),
            GlazeWmConfigPath = @"C:\configs\glazewm.yaml",
            ManagedApps = ["glazewm", "yasb", "zebar", "wallpaper"],
        };
        await _store.UpsertThemeAsync(state, makeActive: true);
        return state;
    }

    [Fact]
    public async Task TurnOff_uses_verified_commands_and_restores_wallpaper()
    {
        var original = CreateFile("original-wall.jpg");
        await SeedEnabledThemeAsync(original);

        var result = await _toggle.TurnOffAsync();

        Assert.True(result.IsSuccess, result.Error);
        await _runner.Received(1).RunAsync("glazewm", "command wm-exit", Arg.Any<CancellationToken>());
        await _runner.Received(1).RunAsync("yasbc", "stop --silent", Arg.Any<CancellationToken>());
        await _runner.Received(1).RunAsync("yasbc", "disable-autostart", Arg.Any<CancellationToken>());
        _wallpaper.Received(1).Set(original);
        Assert.False((await _store.ReadAsync()).ActiveTheme!.IsEnabled);
    }

    [Fact]
    public async Task TurnOff_kills_leftover_zebar_by_pid_only()
    {
        await SeedEnabledThemeAsync();
        _runner.FindProcessIds("zebar").Returns([4242]);

        await _toggle.TurnOffAsync();

        _runner.Received(1).TryKill(4242);
    }

    [Fact]
    public async Task TurnOn_starts_apps_with_saved_config_and_theme_wallpaper()
    {
        var state = await SeedEnabledThemeAsync();
        await _store.UpsertThemeAsync(state with { IsEnabled = false });

        var result = await _toggle.TurnOnAsync();

        Assert.True(result.IsSuccess, result.Error);
        _runner.Received(1).StartDetached("glazewm", $"start --config \"{state.GlazeWmConfigPath}\"");
        _runner.Received(1).StartDetached("yasbc", "start --silent");
        await _runner.Received(1).RunAsync("yasbc", "enable-autostart", Arg.Any<CancellationToken>());
        _wallpaper.Received(1).Set(state.ThemeWallpaperPath!);
        Assert.True((await _store.ReadAsync()).ActiveTheme!.IsEnabled);
    }

    [Fact]
    public async Task TurnOff_without_active_theme_fails()
    {
        var result = await _toggle.TurnOffAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("Nenhum tema ativo", result.Error);
    }

    [Fact]
    public async Task TurnOn_when_already_enabled_fails()
    {
        await SeedEnabledThemeAsync();
        var result = await _toggle.TurnOnAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains("já está ligado", result.Error);
    }

    [Fact]
    public async Task Toggle_pause_uses_glazewm_pause_command()
    {
        var result = await _toggle.TogglePauseAsync();

        Assert.True(result.IsSuccess);
        await _runner.Received(1).RunAsync("glazewm", "command wm-toggle-pause", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_cli_is_reported_not_thrown()
    {
        _runner.RunAsync("glazewm", Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task<ProcessResult>>(_ => throw new System.ComponentModel.Win32Exception("not found"));

        var result = await _toggle.TogglePauseAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("não encontrado", result.Error);
    }

    [Fact]
    public async Task TurnOn_starts_flow_launcher_when_managed_and_not_running()
    {
        var state = await SeedEnabledThemeAsync();
        state = state with { ManagedApps = [.. state.ManagedApps, "flow_launcher"] };
        await _store.UpsertThemeAsync(state with { IsEnabled = false });
        _resolver.Resolve("Flow.Launcher").Returns(@"C:\Users\x\AppData\Local\FlowLauncher\Flow.Launcher.exe");
        _runner.FindProcessIds("Flow.Launcher").Returns([]);

        var result = await _toggle.TurnOnAsync();

        Assert.True(result.IsSuccess, result.Error);
        _runner.Received(1).StartDetached(
            @"C:\Users\x\AppData\Local\FlowLauncher\Flow.Launcher.exe", "");
    }

    [Fact]
    public async Task TurnOn_does_not_start_flow_launcher_again_if_already_running()
    {
        var state = await SeedEnabledThemeAsync();
        state = state with { ManagedApps = [.. state.ManagedApps, "flow_launcher"] };
        await _store.UpsertThemeAsync(state with { IsEnabled = false });
        _runner.FindProcessIds("Flow.Launcher").Returns([777]);

        var result = await _toggle.TurnOnAsync();

        Assert.True(result.IsSuccess, result.Error);
        _runner.DidNotReceive().StartDetached("Flow.Launcher", Arg.Any<string>());
        _runner.DidNotReceive().StartDetached(
            Arg.Is<string>(p => p.Contains("Flow.Launcher.exe")), Arg.Any<string>());
    }

    [Fact]
    public async Task TurnOff_never_kills_flow_launcher()
    {
        var state = await SeedEnabledThemeAsync();
        await _store.UpsertThemeAsync(state with { ManagedApps = [.. state.ManagedApps, "flow_launcher"] });
        _runner.FindProcessIds("Flow.Launcher").Returns([888]);

        await _toggle.TurnOffAsync();

        _runner.DidNotReceive().TryKill(888);
    }

    [Fact]
    public async Task TurnOff_stops_komorebi_when_managed()
    {
        var state = await SeedEnabledThemeAsync();
        await _store.UpsertThemeAsync(state with { ManagedApps = [.. state.ManagedApps, "komorebi"] });

        var result = await _toggle.TurnOffAsync();

        Assert.True(result.IsSuccess, result.Error);
        await _runner.Received(1).RunAsync("komorebic", "stop --whkd", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TurnOff_never_kills_komorebi_by_pid()
    {
        // Komorebi tem stop limpo (restaura janelas) — nunca deveria precisar de fallback
        // de kill por PID como zebar/yasb.
        var state = await SeedEnabledThemeAsync();
        await _store.UpsertThemeAsync(state with { ManagedApps = [.. state.ManagedApps, "komorebi"] });
        _runner.FindProcessIds("komorebi").Returns([9999]);

        await _toggle.TurnOffAsync();

        _runner.DidNotReceive().TryKill(9999);
    }

    [Fact]
    public async Task TurnOn_starts_komorebi_when_managed()
    {
        var state = await SeedEnabledThemeAsync();
        state = state with { ManagedApps = [.. state.ManagedApps, "komorebi"] };
        await _store.UpsertThemeAsync(state with { IsEnabled = false });
        _resolver.Resolve("komorebic").Returns("komorebic");

        var result = await _toggle.TurnOnAsync();

        Assert.True(result.IsSuccess, result.Error);
        _runner.Received(1).StartDetached("komorebic", "start --whkd");
    }

    [Fact]
    public async Task TurnOn_fails_clearly_when_komorebic_not_found()
    {
        var state = await SeedEnabledThemeAsync();
        state = state with { ManagedApps = [.. state.ManagedApps, "komorebi"] };
        await _store.UpsertThemeAsync(state with { IsEnabled = false });
        _resolver.Resolve("komorebic").Returns((string?)null);

        var result = await _toggle.TurnOnAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("Komorebi não encontrado", result.Error);
    }

    [Fact]
    public async Task Preserve_wallpaper_copies_file_into_local_vault()
    {
        var original = CreateFile("wall.png");
        var copy = await _store.PreserveWallpaperAsync(original, "tema-x");

        Assert.NotNull(copy);
        Assert.True(File.Exists(copy));
        File.Delete(original);
        Assert.True(File.Exists(copy)); // cópia sobrevive ao sumiço do original
    }

    [Fact]
    public async Task Activate_turns_off_current_active_theme_and_turns_on_the_target()
    {
        await SeedEnabledThemeAsync(themeId: "tema-a");
        await _store.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "tema-b",
            ThemeName = "Tema B",
            IsEnabled = false,
            ManagedApps = ["glazewm"],
        });

        var result = await _toggle.ActivateAsync("tema-b");

        Assert.True(result.IsSuccess, result.Error);
        await _runner.Received(1).RunAsync("glazewm", "command wm-exit", Arg.Any<CancellationToken>());
        var installed = await _store.ReadAsync();
        Assert.Equal("tema-b", installed.ActiveThemeId);
        Assert.True(installed.Themes["tema-b"].IsEnabled);
        Assert.False(installed.Themes["tema-a"].IsEnabled);
    }

    [Fact]
    public async Task Activate_unknown_theme_fails()
    {
        var result = await _toggle.ActivateAsync("nao-existe");
        Assert.False(result.IsSuccess);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
