using System.Text.Json.Nodes;
using NSubstitute;
using OttoRice.AppRegistry.Appliers;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Tests;

/// <summary>
/// Roda o pipeline real de ponta a ponta sobre o tema de exemplo (examples/blackturq),
/// com todos os caminhos de destino redirecionados para um sandbox. Prova o fluxo
/// completo — planejar, instalar dependências, backup, aplicar, recarregar — e o
/// rollback, sem tocar nas configurações reais da máquina.
/// </summary>
public class EndToEndInstallTests : IDisposable
{
    private readonly string _sandbox = Directory.CreateTempSubdirectory("ottorice-e2e").FullName;
    private readonly string _fakeUserProfile;
    private readonly string _wtSettingsPath;
    private readonly IWinGetClient _winGet = Substitute.For<IWinGetClient>();
    private readonly IWallpaperService _wallpaper = Substitute.For<IWallpaperService>();
    private readonly ITaskbarService _taskbar = Substitute.For<ITaskbarService>();
    private readonly IAppReloader _reloader = Substitute.For<IAppReloader>();
    private readonly BackupSessionStore _backups;

    private static string ExampleThemeDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "examples", "blackturq");
        }
    }

    public EndToEndInstallTests()
    {
        _fakeUserProfile = Path.Combine(_sandbox, "userprofile");
        _backups = new BackupSessionStore(Path.Combine(_sandbox, "backups"));

        var wtDir = Path.Combine(_sandbox, "localappdata", "Microsoft", "Windows Terminal");
        Directory.CreateDirectory(wtDir);
        _wtSettingsPath = Path.Combine(wtDir, "settings.json");
        // settings.json realista: com comentário (JSONC) e ajustes que o usuário não pode perder.
        File.WriteAllText(_wtSettingsPath, """
            {
                // gerado pelo Windows Terminal
                "defaultProfile": "{574e775e-4f2a-5b96-ac1e-a2962a402336}",
                "actions": [ { "command": "paste", "keys": "ctrl+v" } ],
                "profiles": { "list": [ { "name": "Ubuntu (WSL)" } ] }
            }
            """);

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

    private InstallPipeline BuildPipeline(bool failOnReload = false)
    {
        var planner = new TargetPlanner(
            new WindowsTerminalLocator(Path.Combine(_sandbox, "localappdata")),
            path => path.Replace("%USERPROFILE%", _fakeUserProfile));

        IInstallStep[] steps =
        [
            // Dependências antes do Planejamento: reflete a ordem real do App.axaml.cs.
            new DependencyStep(_winGet),
            new PlanStep(planner),
            new BackupStep(_backups, _wallpaper, _taskbar),
            new ApplyStep(new FileOverrideApplier(), new WindowsTerminalApplier(), _wallpaper, _taskbar),
            failOnReload ? new FailingStep() : new ReloadStep(_reloader),
        ];
        return new InstallPipeline(steps);
    }

    private sealed class FailingStep : IInstallStep
    {
        public string Name => "falha simulada";
        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default) =>
            Task.FromResult(Result.Fail("erro apos aplicar"));
    }

    private async Task<(InstallContext Context, Result Result)> RunAsync(bool failOnReload = false)
    {
        var manifestJson = await File.ReadAllTextAsync(
            Path.Combine(ExampleThemeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(manifestJson);
        Assert.True(manifest.IsSuccess, manifest.Error);

        var context = new InstallContext
        {
            Manifest = manifest.Value!,
            ThemeDirectory = ExampleThemeDir,
        };
        var result = await BuildPipeline(failOnReload).RunAsync(context);
        return (context, result);
    }

    private static string VoidhazeThemeDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "examples", "voidhaze");
        }
    }

    /// <summary>
    /// Cobertura do segundo tema de exemplo: instala de ponta a ponta e prova que, por ter
    /// glazewm entre os apps geridos, a taskbar nativa é ocultada automaticamente ao aplicar
    /// (ver ApplyStep) — sem depender de nenhuma ferramenta externa de terceiros.
    /// </summary>
    [Fact]
    public async Task Voidhaze_full_install_writes_every_target_and_hides_the_native_taskbar()
    {
        var manifestJson = await File.ReadAllTextAsync(
            Path.Combine(VoidhazeThemeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(manifestJson);
        Assert.True(manifest.IsSuccess, manifest.Error);

        var context = new InstallContext { Manifest = manifest.Value!, ThemeDirectory = VoidhazeThemeDir };
        var result = await BuildPipeline().RunAsync(context);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(_fakeUserProfile, ".config", "yasb", "config.yaml")));
        _wallpaper.Received(1).Set(Path.Combine(VoidhazeThemeDir, "assets", "wallpaper.png"));
        _taskbar.Received(1).SetAutoHide(true);
    }

    [Fact]
    public async Task Example_theme_manifest_is_valid()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(ExampleThemeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(json);
        Assert.True(manifest.IsSuccess, manifest.Error);
        Assert.Equal("blackturq-minimal", manifest.Value!.ThemeId);
    }

    [Fact]
    public async Task Full_install_writes_every_target_and_preserves_user_terminal_settings()
    {
        var (context, result) = await RunAsync();

        Assert.True(result.IsSuccess, result.Error);

        // GlazeWM e YASB (source de pasta inteira) foram escritos nos caminhos do registry.
        var glazeConfig = Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml");
        Assert.True(File.Exists(glazeConfig));
        Assert.Contains("#40e0d0", await File.ReadAllTextAsync(glazeConfig));

        var yasbDir = Path.Combine(_fakeUserProfile, ".config", "yasb");
        Assert.True(File.Exists(Path.Combine(yasbDir, "config.yaml")));
        Assert.True(File.Exists(Path.Combine(yasbDir, "styles.css")));

        // Windows Terminal: esquema injetado sem perder nada do usuário.
        var wt = JsonNode.Parse(await File.ReadAllTextAsync(_wtSettingsPath))!;
        Assert.Equal("BlackTurq", wt["schemes"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("BlackTurq", wt["profiles"]!["defaults"]!["colorScheme"]!.GetValue<string>());
        Assert.Equal("ctrl+v", wt["actions"]![0]!["keys"]!.GetValue<string>());
        Assert.Equal("Ubuntu (WSL)", wt["profiles"]!["list"]![0]!["name"]!.GetValue<string>());

        _wallpaper.Received(1).Set(Path.Combine(ExampleThemeDir, "assets", "wallpaper.png"));
        Assert.Equal(["glzr-io.glazewm", "AmN.yasb"], context.WingetIdsInstalled);
    }

    [Fact]
    public async Task Failure_after_apply_rolls_everything_back()
    {
        // Estado prévio do usuário nos arquivos que o tema sobrescreveria.
        var glazeConfig = Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(glazeConfig)!);
        await File.WriteAllTextAsync(glazeConfig, "config original do usuario");
        var wtBefore = await File.ReadAllTextAsync(_wtSettingsPath);

        var (_, result) = await RunAsync(failOnReload: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("config original do usuario", await File.ReadAllTextAsync(glazeConfig));
        Assert.Equal(wtBefore, await File.ReadAllTextAsync(_wtSettingsPath));

        // Arquivos que não existiam antes do tema são removidos no rollback.
        Assert.False(File.Exists(Path.Combine(_fakeUserProfile, ".config", "yasb", "styles.css")));

        // Wallpaper volta ao anterior.
        _wallpaper.Received(1).Set(Path.Combine(_sandbox, "wallpaper-antigo.png"));
    }

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);
}
