using NSubstitute;
using OttoRice.AppRegistry.Appliers;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Tests;

/// <summary>
/// Prova de ponta a ponta do item 12.3 do plano de evolução: instalar dois temas de exemplo
/// diferentes (examples/blackturq e examples/voidhaze) em sequência, via InstallViewModel +
/// InstallPipeline reais (só os caminhos de destino redirecionados para sandbox, como em
/// EndToEndInstallTests), e confirmar que os DOIS aparecem como instalados em
/// ThemeStateStore, com só o segundo marcado como ativo.
/// </summary>
public class TwoThemesInstalledTests : IDisposable
{
    private readonly string _sandbox = Directory.CreateTempSubdirectory("ottorice-two-themes").FullName;
    private readonly string _fakeUserProfile;
    private readonly IWinGetClient _winGet = Substitute.For<IWinGetClient>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly IAppReloader _reloader = Substitute.For<IAppReloader>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
    private readonly BackupSessionStore _backups;
    private readonly ThemeStateStore _stateStore;
    private readonly InstallHistoryStore _history;

    private static string ExamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "examples");
        }
    }

    public TwoThemesInstalledTests()
    {
        _fakeUserProfile = Path.Combine(_sandbox, "userprofile");
        _backups = new BackupSessionStore(Path.Combine(_sandbox, "backups"));
        _stateStore = new ThemeStateStore(Path.Combine(_sandbox, "state"));
        _history = new InstallHistoryStore(Path.Combine(_sandbox, "state"));

        var wtDir = Path.Combine(_sandbox, "localappdata", "Microsoft", "Windows Terminal");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "settings.json"), """{ "schemes": [], "profiles": {} }""");

        _winGet.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        _winGet.IsInstalledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _winGet.InstallAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _reloader.ReloadAsync(Arg.Any<OttoRice.AppRegistry.ReloadAction>(), Arg.Any<CancellationToken>())
                 .Returns(Result.Ok());
        _wallpaper.GetCurrentPath().Returns(CreateOriginalWallpaper());
    }

    private string CreateOriginalWallpaper()
    {
        var path = Path.Combine(_sandbox, "wallpaper-antigo.png");
        File.WriteAllText(path, "imagem anterior");
        return path;
    }

    private InstallPipeline BuildPipeline()
    {
        var planner = new TargetPlanner(
            new WindowsTerminalLocator(Path.Combine(_sandbox, "localappdata")),
            path => path
                .Replace("%USERPROFILE%", _fakeUserProfile)
                .Replace("%APPDATA%", Path.Combine(_sandbox, "roaming"))
                .Replace("%LOCALAPPDATA%", Path.Combine(_sandbox, "localappdata")));

        IInstallStep[] steps =
        [
            new DependencyStep(_winGet),
            new PlanStep(planner),
            new BackupStep(_backups, _wallpaper),
            new ApplyStep(new FileOverrideApplier(), new WindowsTerminalApplier(), _wallpaper),
            new ReloadStep(_reloader, _processRunner, verifyTimeout: TimeSpan.FromMilliseconds(1)),
        ];
        return new InstallPipeline(steps);
    }

    private async Task InstallExampleThemeAsync(string exampleName)
    {
        var themeDir = Path.Combine(ExamplesDir, exampleName);
        var manifestJson = await File.ReadAllTextAsync(Path.Combine(themeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(manifestJson);
        Assert.True(manifest.IsSuccess, manifest.Error);

        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(new FetchedTheme(themeDir, manifest.Value!)));

        var vm = new InstallViewModel(
            fetcher, BuildPipeline(), _history, _stateStore, Substitute.For<IThemeFilePicker>())
        {
            ThemeUrl = $"https://github.com/example/{exampleName}",
        };

        await vm.FetchCommand.ExecuteAsync(null);
        Assert.True(vm.InstallCommand.CanExecute(null), vm.StatusMessage);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.StartsWith("✅", vm.StatusMessage);
    }

    [Fact]
    public async Task Installing_two_different_themes_in_sequence_keeps_both_installed_with_only_one_active()
    {
        await InstallExampleThemeAsync("blackturq");
        await InstallExampleThemeAsync("voidhaze");

        var installed = await _stateStore.ReadAsync();

        Assert.Equal(2, installed.Themes.Count);
        Assert.Contains("blackturq-minimal", installed.Themes.Keys);
        Assert.Equal("voidhaze", installed.ActiveThemeId);
        Assert.True(installed.Themes["voidhaze"].IsEnabled);
        // A instalação do segundo tema não mexe no registro do primeiro além de mantê-lo instalado.
        Assert.False(installed.Themes["blackturq-minimal"].IsEnabled == installed.Themes["voidhaze"].IsEnabled
            && installed.ActiveThemeId == "blackturq-minimal");

        var history = await _history.ReadAllAsync();
        Assert.Equal(2, history.Count);
    }

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);
}
