using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Tests;

/// <summary>
/// PlanStep resolvendo a paleta ativa (seção 13 da doc "OttoRice") a partir de
/// <see cref="InstallContext.PaletteId"/> contra <see cref="RiceManifest.Palettes"/>,
/// antes de repassar o <c>sourceOverride</c> pro <see cref="TargetPlanner"/>.
/// </summary>
public class PlanStepTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-planstep").FullName;
    private readonly string _themeDir;
    private readonly string _fakeUserProfile;
    private readonly TargetPlanner _planner;

    public PlanStepTests()
    {
        _themeDir = Path.Combine(_dir, "theme");
        _fakeUserProfile = Path.Combine(_dir, "userprofile");
        Directory.CreateDirectory(_themeDir);

        _planner = new TargetPlanner(
            new WindowsTerminalLocator(Path.Combine(_dir, "localappdata")),
            path => path.Replace("%USERPROFILE%", _fakeUserProfile));
    }

    private string WriteThemeFile(string relative, string content)
    {
        var path = Path.Combine(_themeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private RiceManifest ManifestWithPalette() => new()
    {
        SchemaVersion = "1.0",
        ThemeId = "t",
        Name = "T",
        Targets = [new RiceTarget { App = "glazewm", Action = "override", Source = "configs/glazewm/config.yaml" }],
        Palettes = [new RicePalette { Id = "latte", Name = "Latte", SourceOverride = "palettes/latte" }],
    };

    [Fact]
    public async Task Resolves_the_palette_directory_when_context_has_a_matching_palette_id()
    {
        WriteThemeFile("configs/glazewm/config.yaml", "mocha");
        WriteThemeFile("palettes/latte/configs/glazewm/config.yaml", "latte");
        var context = new InstallContext
        {
            Manifest = ManifestWithPalette(),
            ThemeDirectory = _themeDir,
            PaletteId = "latte",
        };

        var result = await new PlanStep(_planner).ExecuteAsync(context);

        Assert.True(result.IsSuccess, result.Error);
        var op = Assert.Single(context.Operations);
        Assert.Equal("latte", File.ReadAllText(op.SourcePath));
    }

    [Fact]
    public async Task Falls_back_to_default_when_palette_id_is_unknown_to_the_manifest()
    {
        // Manifesto mudou desde a instalação (ex.: paleta removida do repo) — não deve
        // travar a reaplicação, só usar a paleta padrão.
        WriteThemeFile("configs/glazewm/config.yaml", "mocha");
        var context = new InstallContext
        {
            Manifest = ManifestWithPalette(),
            ThemeDirectory = _themeDir,
            PaletteId = "nao-existe-mais",
        };

        var result = await new PlanStep(_planner).ExecuteAsync(context);

        Assert.True(result.IsSuccess, result.Error);
        var op = Assert.Single(context.Operations);
        Assert.Equal("mocha", File.ReadAllText(op.SourcePath));
    }

    [Fact]
    public async Task Null_palette_id_always_uses_the_default_source()
    {
        WriteThemeFile("configs/glazewm/config.yaml", "mocha");
        WriteThemeFile("palettes/latte/configs/glazewm/config.yaml", "latte");
        var context = new InstallContext
        {
            Manifest = ManifestWithPalette(),
            ThemeDirectory = _themeDir,
            PaletteId = null,
        };

        var result = await new PlanStep(_planner).ExecuteAsync(context);

        Assert.True(result.IsSuccess, result.Error);
        var op = Assert.Single(context.Operations);
        Assert.Equal("mocha", File.ReadAllText(op.SourcePath));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
