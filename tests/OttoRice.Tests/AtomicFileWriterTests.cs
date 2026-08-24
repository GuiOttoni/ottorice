using OttoRice.Common;

namespace OttoRice.Tests;

public class AtomicFileWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-io-tests").FullName;

    [Fact]
    public async Task Writes_and_overwrites_without_leaving_tmp()
    {
        var target = Path.Combine(_dir, "sub", "config.yaml");

        await AtomicFileWriter.WriteAllTextAsync(target, "v1");
        await AtomicFileWriter.WriteAllTextAsync(target, "v2");

        Assert.Equal("v2", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public async Task Copy_replicates_source_atomically()
    {
        var source = Path.Combine(_dir, "tema.css");
        File.WriteAllText(source, "body { color: #40E0D0; }");
        var target = Path.Combine(_dir, "destino", "styles.css");

        await AtomicFileWriter.CopyAsync(source, target);

        Assert.Equal(File.ReadAllText(source), File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
