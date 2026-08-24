using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;

namespace OttoRice.Tests;

public class TargetPlannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-planner").FullName;
    private readonly string _themeDir;
    private readonly string _fakeUserProfile;
    private readonly TargetPlanner _planner;

    public TargetPlannerTests()
    {
        _themeDir = Path.Combine(_dir, "theme");
        _fakeUserProfile = Path.Combine(_dir, "userprofile");
        Directory.CreateDirectory(_themeDir);

        // Locator aponta para um LocalAppData fake com WT "instalado".
        var wtDir = Path.Combine(_dir, "localappdata", "Microsoft", "Windows Terminal");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "settings.json"), "{}");

        _planner = new TargetPlanner(
            new WindowsTerminalLocator(Path.Combine(_dir, "localappdata")),
            path => path.Replace("%USERPROFILE%", _fakeUserProfile));
    }

    private string WriteThemeFile(string relative, string content = "x")
    {
        var path = Path.Combine(_themeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static RiceManifest Manifest(params RiceTarget[] targets) => new()
    {
        SchemaVersion = "1.0",
        ThemeId = "t",
        Name = "T",
        Targets = [.. targets],
    };

    [Fact]
    public void File_override_maps_to_registry_config_path()
    {
        WriteThemeFile("configs/config.yaml");
        var manifest = Manifest(new RiceTarget { App = "glazewm", Action = "override", Source = "configs/config.yaml" });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        var op = Assert.Single(plan.Value!);
        Assert.Equal(Path.Combine(_fakeUserProfile, ".glzr", "glazewm", "config.yaml"), op.TargetPath);
    }

    [Fact]
    public void Directory_override_maps_each_file_into_config_root()
    {
        WriteThemeFile("configs/yasb/config.yaml");
        WriteThemeFile("configs/yasb/styles.css");
        var manifest = Manifest(new RiceTarget { App = "yasb", Action = "override", Source = "configs/yasb" });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal(2, plan.Value!.Count);
        Assert.All(plan.Value!, op =>
            Assert.StartsWith(Path.Combine(_fakeUserProfile, ".config", "yasb"), op.TargetPath));
    }

    [Fact]
    public void Merge_scheme_resolves_windows_terminal_via_locator()
    {
        WriteThemeFile("wt-scheme.json");
        var manifest = Manifest(new RiceTarget { App = "windows_terminal", Action = "merge_scheme", Source = "wt-scheme.json" });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.EndsWith("settings.json", Assert.Single(plan.Value!).TargetPath);
    }

    [Fact]
    public void Wallpaper_set_produces_operation_without_target_path()
    {
        WriteThemeFile("assets/wall.png");
        var manifest = Manifest(new RiceTarget { App = "wallpaper", Action = "set", Source = "assets/wall.png" });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Equal("", Assert.Single(plan.Value!).TargetPath);
    }

    [Fact]
    public void Missing_source_file_fails()
    {
        var manifest = Manifest(new RiceTarget { App = "glazewm", Action = "override", Source = "nao-existe.yaml" });
        Assert.False(_planner.Build(manifest, _themeDir).IsSuccess);
    }

    [Fact]
    public void File_override_with_unrecognized_name_lands_under_config_root_not_a_wrong_known_file()
    {
        // Regressão: antes, um arquivo de tema sem nome reconhecido caía silenciosamente no
        // primeiro ConfigPaths do app — para YASB isso significava sobrescrever config.yaml
        // com o conteúdo de um arquivo com nome errado. Agora ele nunca "adivinha" um dos
        // ConfigPaths: cai no ConfigRoot com o próprio nome (arquivo extra, não substitui
        // nada existente).
        WriteThemeFile("configs/style.css"); // nome errado — YASB espera "styles.css"
        var manifest = Manifest(new RiceTarget { App = "yasb", Action = "override", Source = "configs/style.css" });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        var op = Assert.Single(plan.Value!);
        Assert.Equal(Path.Combine(_fakeUserProfile, ".config", "yasb", "style.css"), op.TargetPath);
        Assert.NotEqual(Path.Combine(_fakeUserProfile, ".config", "yasb", "config.yaml"), op.TargetPath);
    }

    [Fact]
    public void Vscode_file_override_maps_to_registry_config_path()
    {
        WriteThemeFile("configs/vscode-settings.json");
        var manifest = Manifest(new RiceTarget
        {
            App = "vscode", Action = "override", Source = "configs/vscode-settings.json",
        });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        // vscode-settings.json não bate por nome com "settings.json" — cai no ConfigRoot
        // com o próprio nome, não sobrescreve o settings.json do usuário silenciosamente.
        Assert.EndsWith(@"Code\User\vscode-settings.json", Assert.Single(plan.Value!).TargetPath);
    }

    [Fact]
    public void OhMyPosh_theme_with_no_fixed_config_path_lands_under_its_own_config_root()
    {
        // oh_my_posh não tem ConfigPaths (o tema não tem nome/local fixo que a ferramenta
        // leia sozinha) — deve sempre cair no ConfigRoot próprio do OttoRice.
        WriteThemeFile("configs/catppuccin.omp.json");
        var manifest = Manifest(new RiceTarget
        {
            App = "oh_my_posh", Action = "override", Source = "configs/catppuccin.omp.json",
        });

        var plan = _planner.Build(manifest, _themeDir);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.EndsWith(@"OttoRice\ohmyposh\catppuccin.omp.json", Assert.Single(plan.Value!).TargetPath);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
