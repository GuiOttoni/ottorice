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
    private readonly IAppReloader _reloader = Substitute.For<IAppReloader>();
    private readonly IProcessRunner _processRunner = Substitute.For<IProcessRunner>();
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
            path => path
                .Replace("%USERPROFILE%", _fakeUserProfile)
                .Replace("%APPDATA%", Path.Combine(_sandbox, "roaming"))
                .Replace("%LOCALAPPDATA%", Path.Combine(_sandbox, "localappdata")));

        IInstallStep[] steps =
        [
            // Dependências antes do Planejamento: reflete a ordem real do App.axaml.cs.
            new DependencyStep(_winGet),
            new PlanStep(planner),
            new BackupStep(_backups, _wallpaper),
            new ApplyStep(new FileOverrideApplier(), new WindowsTerminalApplier(), _wallpaper),
            failOnReload
                ? new FailingStep()
                : new ReloadStep(_reloader, _processRunner, verifyTimeout: TimeSpan.FromMilliseconds(1)),
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

    /// <summary>Cobertura do segundo tema de exemplo: instala de ponta a ponta.</summary>
    [Fact]
    public async Task Voidhaze_full_install_writes_every_target()
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
    }

    private static string CatppuccinThemeDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "examples", "catppuccin");
        }
    }

    /// <summary>
    /// Terceiro tema de exemplo: cobre os cinco apps novos (vscode, zed, fastfetch,
    /// flow_launcher, oh_my_posh), todos resolvidos via %APPDATA%/%LOCALAPPDATA%
    /// (não só %USERPROFILE% como glazewm/yasb).
    /// </summary>
    [Fact]
    public async Task Catppuccin_full_install_writes_every_target_including_the_new_apps()
    {
        var manifestJson = await File.ReadAllTextAsync(
            Path.Combine(CatppuccinThemeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(manifestJson);
        Assert.True(manifest.IsSuccess, manifest.Error);

        var context = new InstallContext { Manifest = manifest.Value!, ThemeDirectory = CatppuccinThemeDir };
        var result = await BuildPipeline().RunAsync(context);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(File.Exists(Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(_fakeUserProfile, ".config", "yasb", "config.yaml")));
        Assert.True(File.Exists(Path.Combine(_sandbox, "roaming", "Code", "User", "settings.json")));
        Assert.True(File.Exists(Path.Combine(_sandbox, "roaming", "Zed", "settings.json")));
        Assert.True(File.Exists(Path.Combine(_sandbox, "roaming", "fastfetch", "config.jsonc")));
        Assert.True(File.Exists(Path.Combine(_sandbox, "roaming", "FlowLauncher", "Settings", "Settings.json")));
        Assert.True(File.Exists(Path.Combine(
            _sandbox, "localappdata", "OttoRice", "ohmyposh", "catppuccin-mocha.omp.json")));
        _wallpaper.Received(1).Set(Path.Combine(CatppuccinThemeDir, "assets", "wallpaper.png"));
        Assert.Equal(
            ["glzr-io.glazewm", "AmN.yasb", "Microsoft.VisualStudioCode", "ZedIndustries.Zed",
             "Fastfetch-cli.Fastfetch", "Flow-Launcher.Flow-Launcher", "JanDeDobbeleer.OhMyPosh"],
            context.WingetIdsInstalled);
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

    /// <summary>
    /// Toggle por componente (RF: desligar/ligar cada target na instalação): desmarcar
    /// YASB e Windows Terminal na prévia faz o pipeline instalar só GlazeWM e wallpaper —
    /// os outros dois nunca são planejados nem aplicados (o config do YASB nem chega a
    /// existir, e o settings.json do Windows Terminal do usuário fica intocado).
    /// </summary>
    [Fact]
    public async Task Selected_target_indexes_narrow_the_install_to_only_those_targets()
    {
        var manifestJson = await File.ReadAllTextAsync(
            Path.Combine(ExampleThemeDir, ThemeFetcher.ManifestFileName));
        var manifest = ManifestValidator.Parse(manifestJson);
        Assert.True(manifest.IsSuccess, manifest.Error);
        // targets: 0=glazewm, 1=yasb, 2=windows_terminal, 3=wallpaper — mantém só 0 e 3.
        Assert.Equal("yasb", manifest.Value!.Targets[1].App);
        Assert.Equal("windows_terminal", manifest.Value!.Targets[2].App);

        var wtBefore = await File.ReadAllTextAsync(_wtSettingsPath);

        var context = new InstallContext
        {
            Manifest = manifest.Value!,
            ThemeDirectory = ExampleThemeDir,
            SelectedTargetIndexes = new HashSet<int> { 0, 3 },
        };
        var result = await BuildPipeline().RunAsync(context);

        Assert.True(result.IsSuccess, result.Error);

        var glazeConfig = Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml");
        Assert.True(File.Exists(glazeConfig));
        _wallpaper.Received(1).Set(Path.Combine(ExampleThemeDir, "assets", "wallpaper.png"));

        // YASB nunca foi planejado nem aplicado — a pasta de config nem chega a existir.
        Assert.False(File.Exists(Path.Combine(_fakeUserProfile, ".config", "yasb", "config.yaml")));
        Assert.False(Directory.Exists(Path.Combine(_fakeUserProfile, ".config", "yasb")));

        // Windows Terminal: settings.json do usuário intocado (nenhum esquema injetado).
        Assert.Equal(wtBefore, await File.ReadAllTextAsync(_wtSettingsPath));

        // Só 2 operações planejadas (GlazeWM + wallpaper) — YASB e Windows Terminal nunca
        // chegaram ao TargetPlanner/ApplyStep.
        Assert.Equal(2, context.Operations.Count);

        // Decisão documentada (ver InstallPipeline/DependencyStep): dependências do WinGet não
        // são filtradas pela seleção de targets — o manifesto não amarra dependency[] a um
        // target específico (schema atual não tem esse vínculo), então ambas continuam
        // instaladas mesmo com o target do YASB desmarcado.
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
