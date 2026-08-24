using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Tests;

public class ManifestValidatorTests
{
    private static RiceManifest ValidManifest() => new()
    {
        SchemaVersion = "1.0",
        ThemeId = "blackturq-minimal",
        Name = "BlackTurq Windows Rice",
        Author = "comunidade",
        Dependencies = [new RiceDependency { WingetId = "glazewm.glazewm" }],
        Targets =
        [
            new RiceTarget { App = "glazewm", Action = "override", Source = "configs/glazewm/config.yaml" },
            new RiceTarget { App = "windows_terminal", Action = "merge_scheme", Source = "configs/wt-scheme.json", SetAsDefault = true },
        ],
    };

    [Fact]
    public void Valid_manifest_passes()
    {
        Assert.Empty(ManifestValidator.Validate(ValidManifest()));
    }

    [Fact]
    public void Wrong_schema_version_fails()
    {
        var errors = ManifestValidator.Validate(ValidManifest() with { SchemaVersion = "2.0" });
        Assert.Contains(errors, e => e.Contains("schemaVersion"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Não Kebab")]
    [InlineData("UPPER-case")]
    public void Invalid_theme_id_fails(string? themeId)
    {
        var errors = ManifestValidator.Validate(ValidManifest() with { ThemeId = themeId });
        Assert.Contains(errors, e => e.Contains("themeId"));
    }

    [Fact]
    public void Empty_targets_fails()
    {
        var errors = ManifestValidator.Validate(ValidManifest() with { Targets = [] });
        Assert.Contains(errors, e => e.Contains("ao menos um target"));
    }

    [Fact]
    public void Unsupported_app_fails()
    {
        var manifest = ValidManifest() with
        {
            Targets = [new RiceTarget { App = "regedit", Action = "override", Source = "x.reg" }],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("não é suportado"));
    }

    [Fact]
    public void Action_not_allowed_for_app_fails()
    {
        var manifest = ValidManifest() with
        {
            Targets = [new RiceTarget { App = "glazewm", Action = "merge_scheme", Source = "a.yaml" }],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("não permitida"));
    }

    [Theory]
    [InlineData("../fora-do-repo.yaml")]
    [InlineData("configs/../../escape.yaml")]
    [InlineData(@"C:\Windows\evil.yaml")]
    [InlineData("/etc/passwd")]
    [InlineData(null)]
    public void Unsafe_source_path_fails(string? source)
    {
        var manifest = ValidManifest() with
        {
            Targets = [new RiceTarget { App = "glazewm", Action = "override", Source = source }],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("source"));
    }

    [Fact]
    public void Invalid_winget_id_fails()
    {
        var manifest = ValidManifest() with
        {
            Dependencies = [new RiceDependency { WingetId = "glazewm; rm -rf" }],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("wingetId"));
    }

    [Fact]
    public void Parse_accepts_json_with_comments_and_validates()
    {
        const string json = """
            {
              // manifesto de exemplo
              "schemaVersion": "1.0",
              "themeId": "tema-teste",
              "name": "Tema Teste",
              "targets": [
                { "app": "glazewm", "action": "override", "source": "configs/config.yaml" },
              ]
            }
            """;
        var result = ManifestValidator.Parse(json);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("tema-teste", result.Value!.ThemeId);
    }

    [Fact]
    public void Configure_mod_without_settings_passes()
    {
        // settings é opcional: sem ele, só instala/habilita o mod com os valores default.
        var manifest = ValidManifest() with
        {
            Targets = [new RiceTarget { App = "windows-11-taskbar-styler", Action = "configure_mod" }],
        };
        Assert.Empty(ManifestValidator.Validate(manifest));
    }

    [Fact]
    public void Configure_mod_with_valid_settings_passes()
    {
        var manifest = ValidManifest() with
        {
            Targets =
            [
                new RiceTarget
                {
                    App = "windows-11-taskbar-styler",
                    Action = "configure_mod",
                    Settings = new() { ["theme"] = "FrostyGlass", ["styleConstants[0]"] = "accent: #cba6f7" },
                },
            ],
        };
        Assert.Empty(ManifestValidator.Validate(manifest));
    }

    [Theory]
    [InlineData("theme; rm -rf")]
    [InlineData("theme value")]
    public void Configure_mod_with_unsafe_settings_key_fails(string key)
    {
        var manifest = ValidManifest() with
        {
            Targets =
            [
                new RiceTarget
                {
                    App = "windows-11-taskbar-styler",
                    Action = "configure_mod",
                    Settings = new() { [key] = "x" },
                },
            ],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("chave"));
    }

    [Theory]
    [InlineData("ok\" & del /f /q C:\\* & \"")]
    [InlineData("line1\nline2")]
    [InlineData("100% & calc")]
    public void Configure_mod_with_unsafe_settings_value_fails(string value)
    {
        var manifest = ValidManifest() with
        {
            Targets =
            [
                new RiceTarget
                {
                    App = "windows-11-taskbar-styler",
                    Action = "configure_mod",
                    Settings = new() { ["theme"] = value },
                },
            ],
        };
        Assert.Contains(ManifestValidator.Validate(manifest), e => e.Contains("valor"));
    }

    [Fact]
    public void Parse_rejects_malformed_json()
    {
        var result = ManifestValidator.Parse("{ isso não é json");
        Assert.False(result.IsSuccess);
        Assert.Contains("JSON", result.Error);
    }
}
