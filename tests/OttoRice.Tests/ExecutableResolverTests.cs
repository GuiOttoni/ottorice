using OttoRice.Common;

namespace OttoRice.Tests;

public class ExecutableResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-resolver").FullName;
    private readonly string? _previousPath = Environment.GetEnvironmentVariable("PATH");

    public ExecutableResolverTests()
    {
        // Isola do PATH real da máquina de dev: sem isso, ferramentas realmente
        // instaladas (ex.: GlazeWM/YASB de um dogfooding anterior) mascaram os cenários
        // "não está no PATH" que estes testes existem para cobrir.
        Environment.SetEnvironmentVariable("PATH", "");
    }

    private string CreateExe(string subDir, string name)
    {
        var dir = Path.Combine(_dir, subDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "fake exe");
        return path;
    }

    /// <summary>Resolver sem acesso a registro, com %ProgramFiles% apontando para o sandbox.</summary>
    private ExecutableResolver Resolver() => new(
        expand: p => p.Replace("%ProgramFiles%", Path.Combine(_dir, "ProgramFiles")),
        isWindows: () => false);

    [Fact]
    public void Finds_glazewm_in_known_install_location_when_not_on_path()
    {
        var expected = CreateExe(@"ProgramFiles\glzr.io\GlazeWM\cli", "glazewm.exe");

        // Cenário real observado: WinGet instala em Program Files e não mexe no PATH.
        Assert.Equal(expected, Resolver().Resolve("glazewm"));
    }

    [Fact]
    public void Finds_yasbc_in_known_install_location()
    {
        var expected = CreateExe(@"ProgramFiles\YASB", "yasbc.exe");
        Assert.Equal(expected, Resolver().Resolve("yasbc"));
    }

    [Fact]
    public void Prefers_path_entry_over_known_location()
    {
        CreateExe(@"ProgramFiles\glzr.io\GlazeWM\cli", "glazewm.exe");
        var onPath = CreateExe("bin", "glazewm.exe");

        var previous = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.Combine(_dir, "bin"));
            Assert.Equal(onPath, Resolver().Resolve("glazewm"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
        }
    }

    [Fact]
    public void Returns_null_when_tool_is_nowhere()
    {
        Assert.Null(Resolver().Resolve("glazewm"));
    }

    [Fact]
    public void Unknown_tool_without_path_entry_returns_null()
    {
        Assert.Null(Resolver().Resolve("ferramenta-inexistente"));
    }

    [Fact]
    public void Accepts_name_with_or_without_exe_suffix()
    {
        var expected = CreateExe(@"ProgramFiles\YASB", "yasbc.exe");
        Assert.Equal(expected, Resolver().Resolve("yasbc.exe"));
        Assert.Equal(expected, Resolver().Resolve("yasbc"));
    }

    [Fact]
    public void Malformed_path_entry_does_not_throw()
    {
        var expected = CreateExe(@"ProgramFiles\YASB", "yasbc.exe");
        var previous = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "caminho|invalido<>;");
            Assert.Equal(expected, Resolver().Resolve("yasbc"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath);
        Directory.Delete(_dir, recursive: true);
    }
}
