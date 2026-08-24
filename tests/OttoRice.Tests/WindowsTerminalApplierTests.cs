using System.Text.Json.Nodes;
using OttoRice.AppRegistry.Appliers;

namespace OttoRice.Tests;

public class WindowsTerminalApplierTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-wt-tests").FullName;
    private readonly WindowsTerminalApplier _applier = new();

    private const string Scheme = """{ "name": "BlackTurq", "background": "#000000", "foreground": "#40E0D0" }""";

    private string WriteSettings(string content)
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Adds_scheme_and_preserves_user_settings()
    {
        var path = WriteSettings("""
            {
                "defaultProfile": "{guid}",
                "actions": [ { "command": "paste", "keys": "ctrl+v" } ],
                "profiles": { "list": [ { "name": "WSL Ubuntu" } ] }
            }
            """);

        await _applier.InjectColorSchemeAsync(path, Scheme, setAsDefault: false);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("BlackTurq", root["schemes"]![0]!["name"]!.GetValue<string>());
        // Nada do usuário pode se perder no merge:
        Assert.Equal("{guid}", root["defaultProfile"]!.GetValue<string>());
        Assert.Equal("ctrl+v", root["actions"]![0]!["keys"]!.GetValue<string>());
        Assert.Equal("WSL Ubuntu", root["profiles"]!["list"]![0]!["name"]!.GetValue<string>());
        Assert.Null(root["profiles"]!["defaults"]);
    }

    [Fact]
    public async Task Updates_existing_scheme_without_duplicating()
    {
        var path = WriteSettings("""
            { "schemes": [ { "name": "BlackTurq", "background": "#111111" }, { "name": "Outro" } ] }
            """);

        await _applier.InjectColorSchemeAsync(path, Scheme, setAsDefault: false);

        var schemes = JsonNode.Parse(File.ReadAllText(path))!["schemes"]!.AsArray();
        Assert.Equal(2, schemes.Count);
        Assert.Equal("#000000", schemes.First(s => s!["name"]!.GetValue<string>() == "BlackTurq")!["background"]!.GetValue<string>());
    }

    [Fact]
    public async Task Set_as_default_writes_profiles_defaults()
    {
        var path = WriteSettings("""{ "profiles": { "list": [] } }""");

        await _applier.InjectColorSchemeAsync(path, Scheme, setAsDefault: true);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("BlackTurq", root["profiles"]!["defaults"]!["colorScheme"]!.GetValue<string>());
    }

    [Fact]
    public async Task Accepts_jsonc_comments_in_settings()
    {
        var path = WriteSettings("""
            {
                // comentário gerado pelo Windows Terminal
                "defaultProfile": "{guid}",
            }
            """);

        await _applier.InjectColorSchemeAsync(path, Scheme, setAsDefault: false);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("{guid}", root["defaultProfile"]!.GetValue<string>());
    }

    [Fact]
    public async Task Missing_file_throws()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _applier.InjectColorSchemeAsync(Path.Combine(_dir, "nao-existe.json"), Scheme, false));
    }

    [Fact]
    public async Task Scheme_without_name_throws()
    {
        var path = WriteSettings("{}");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _applier.InjectColorSchemeAsync(path, """{ "background": "#000" }""", false));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
