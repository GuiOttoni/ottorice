using System.IO.Compression;
using System.Net;
using System.Text;
using OttoRice.Features.ThemeImport;

namespace OttoRice.Tests;

public class GitHubRepoRefTests
{
    [Theory]
    [InlineData("https://github.com/ashish0kumar/windots", "ashish0kumar", "windots", null, "")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo", null, "")]
    [InlineData("https://github.com/owner/repo/", "owner", "repo", null, "")]
    [InlineData("https://github.com/owner/repo/tree/main/themes/blackturq", "owner", "repo", "main", "themes/blackturq")]
    [InlineData("https://github.com/owner/repo/tree/dev", "owner", "repo", "dev", "")]
    public void Parses_valid_urls(string url, string owner, string repo, string? branch, string subPath)
    {
        var result = GitHubRepoRef.Parse(url);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new GitHubRepoRef(owner, repo, branch, subPath), result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é url")]
    [InlineData("http://github.com/owner/repo")]
    [InlineData("https://gitlab.com/owner/repo")]
    [InlineData("https://github.com/apenas-owner")]
    [InlineData("https://github.com/owner/repo/blob/main/arquivo.md")]
    public void Rejects_invalid_urls(string url)
    {
        Assert.False(GitHubRepoRef.Parse(url).IsSuccess);
    }
}

public class ThemeFetcherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-fetcher").FullName;

    private const string ValidManifest = """
        {
          "schemaVersion": "1.0",
          "themeId": "tema-zip",
          "name": "Tema do Zip",
          "targets": [ { "app": "glazewm", "action": "override", "source": "configs/config.yaml" } ]
        }
        """;

    /// <summary>Monta um zipball no formato do GitHub: um diretório-raiz "repo-sha/".</summary>
    private static byte[] BuildZipball(Dictionary<string, string> files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = zip.CreateEntry($"repo-abc123/{path}");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        return stream.ToArray();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private ThemeFetcher Fetcher(FakeHandler handler) =>
        new(new HttpClient(handler), Path.Combine(_dir, "cache"));

    [Fact]
    public async Task Downloads_extracts_and_validates_manifest()
    {
        var zip = BuildZipball(new()
        {
            ["rice-manifest.json"] = ValidManifest,
            ["configs/config.yaml"] = "gaps: 8",
        });
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        });

        var result = await Fetcher(handler).FetchAsync("https://github.com/owner/repo");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("tema-zip", result.Value!.Manifest.ThemeId);
        Assert.True(File.Exists(Path.Combine(result.Value.ThemeDirectory, "configs", "config.yaml")));
    }

    [Fact]
    public async Task Falls_back_from_main_to_master_branch()
    {
        var zip = BuildZipball(new() { ["rice-manifest.json"] = ValidManifest });
        var handler = new FakeHandler(req =>
            req.RequestUri!.ToString().EndsWith("/main")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        var result = await Fetcher(handler).FetchAsync("https://github.com/owner/repo");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(handler.Urls, u => u.EndsWith("/master"));
    }

    [Fact]
    public async Task Uses_subpath_from_tree_url()
    {
        var zip = BuildZipball(new()
        {
            ["themes/dark/rice-manifest.json"] = ValidManifest,
            ["themes/dark/configs/config.yaml"] = "x",
        });
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        });

        var result = await Fetcher(handler).FetchAsync("https://github.com/owner/repo/tree/main/themes/dark");

        Assert.True(result.IsSuccess, result.Error);
        Assert.EndsWith(Path.Combine("themes", "dark"), result.Value!.ThemeDirectory);
    }

    [Fact]
    public async Task Missing_manifest_in_repo_fails_with_clear_message()
    {
        var zip = BuildZipball(new() { ["readme.md"] = "sem manifesto" });
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zip),
        });

        var result = await Fetcher(handler).FetchAsync("https://github.com/owner/repo");

        Assert.False(result.IsSuccess);
        Assert.Contains("rice-manifest.json", result.Error);
    }

    [Fact]
    public async Task Reads_theme_from_local_folder_without_network()
    {
        var themeDir = Path.Combine(_dir, "meu-tema");
        Directory.CreateDirectory(themeDir);
        await File.WriteAllTextAsync(Path.Combine(themeDir, "rice-manifest.json"), ValidManifest);

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var result = await Fetcher(handler).FetchAsync(themeDir);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("tema-zip", result.Value!.Manifest.ThemeId);
        Assert.Equal(themeDir, result.Value.ThemeDirectory);
        Assert.Empty(handler.Urls); // nenhum request de rede
    }

    [Fact]
    public async Task Local_folder_without_manifest_fails()
    {
        var themeDir = Path.Combine(_dir, "pasta-vazia");
        Directory.CreateDirectory(themeDir);

        var result = await Fetcher(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))
            .FetchAsync(themeDir);

        Assert.False(result.IsSuccess);
        Assert.Contains("rice-manifest.json", result.Error);
    }

    [Fact]
    public async Task Repo_not_found_fails()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var result = await Fetcher(handler).FetchAsync("https://github.com/owner/inexistente");
        Assert.False(result.IsSuccess);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
