using OttoRice.Common;

namespace OttoRice.Tests;

public class PathResolverTests
{
    [Fact]
    public void Expands_environment_variables()
    {
        var expanded = PathResolver.Expand(@"%USERPROFILE%\.glzr\glazewm\config.yaml");
        Assert.DoesNotContain("%", expanded);
        Assert.EndsWith(@"\.glzr\glazewm\config.yaml", expanded);
    }
}

public class WindowsTerminalLocatorTests : IDisposable
{
    private readonly string _fakeLocalAppData = Directory.CreateTempSubdirectory("ottorice-wtloc").FullName;

    [Fact]
    public void Finds_packaged_store_installation()
    {
        var localState = Path.Combine(
            _fakeLocalAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState");
        Directory.CreateDirectory(localState);
        var settings = Path.Combine(localState, "settings.json");
        File.WriteAllText(settings, "{}");

        var found = new WindowsTerminalLocator(_fakeLocalAppData).FindSettingsPath();
        Assert.Equal(settings, found);
    }

    [Fact]
    public void Falls_back_to_unpackaged_installation()
    {
        var dir = Path.Combine(_fakeLocalAppData, "Microsoft", "Windows Terminal");
        Directory.CreateDirectory(dir);
        var settings = Path.Combine(dir, "settings.json");
        File.WriteAllText(settings, "{}");

        var found = new WindowsTerminalLocator(_fakeLocalAppData).FindSettingsPath();
        Assert.Equal(settings, found);
    }

    [Fact]
    public void Returns_null_when_not_installed()
    {
        Assert.Null(new WindowsTerminalLocator(_fakeLocalAppData).FindSettingsPath());
    }

    public void Dispose() => Directory.Delete(_fakeLocalAppData, recursive: true);
}
