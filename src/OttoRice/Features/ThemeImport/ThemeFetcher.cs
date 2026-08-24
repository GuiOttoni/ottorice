using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeImport;

public sealed record FetchedTheme(string ThemeDirectory, RiceManifest Manifest);

public sealed record GitHubRepoRef(string Owner, string Repo, string? Branch, string SubPath)
{
    /// <summary>
    /// Aceita: https://github.com/owner/repo[.git], https://github.com/owner/repo/tree/branch[/sub/path].
    /// </summary>
    public static Result<GitHubRepoRef> Parse(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return Result<GitHubRepoRef>.Fail("URL inválida — use um repositório https://github.com/owner/repo.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return Result<GitHubRepoRef>.Fail("URL precisa apontar para um repositório (owner/repo).");

        var owner = segments[0];
        var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];

        string? branch = null;
        var subPath = "";
        if (segments.Length >= 4 && segments[2] == "tree")
        {
            branch = segments[3];
            subPath = string.Join('/', segments.Skip(4));
        }
        else if (segments.Length > 2)
        {
            return Result<GitHubRepoRef>.Fail("URL não reconhecida — use a raiz do repo ou um link /tree/branch/pasta.");
        }

        return Result<GitHubRepoRef>.Ok(new GitHubRepoRef(owner, repo, branch, subPath));
    }
}

public interface IThemeFetcher
{
    Task<Result<FetchedTheme>> FetchAsync(string repoUrl, CancellationToken ct = default);
}

/// <summary>
/// Baixa o repositório do tema como zipball (codeload — sem rate limit da API), extrai
/// para o cache local e valida o rice-manifest.json. Um único request cobre sources de
/// arquivo e de pasta.
/// </summary>
public sealed class ThemeFetcher(
    HttpClient http, string? cacheRootOverride = null, ILogger<ThemeFetcher>? logger = null) : IThemeFetcher
{
    public const string ManifestFileName = "rice-manifest.json";
    private const long MaxDownloadBytes = 50 * 1024 * 1024; // RNF-01

    private readonly string _cacheRoot = cacheRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OttoRice", "cache");

    public async Task<Result<FetchedTheme>> FetchAsync(string repoUrl, CancellationToken ct = default)
    {
        // Fontes locais: como um autor testa o próprio tema antes de publicar no GitHub.
        // Aceita a pasta do tema ou o próprio arquivo de manifesto (.json).
        var local = repoUrl?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(local))
        {
            if (Directory.Exists(local))
                return await ReadLocalThemeAsync(local, ct);

            if (File.Exists(local))
                return await ReadLocalManifestFileAsync(local, ct);
        }

        var parsed = GitHubRepoRef.Parse(repoUrl);
        if (!parsed.IsSuccess)
            return Result<FetchedTheme>.Fail(parsed.Error!);
        var repoRef = parsed.Value!;

        var branches = repoRef.Branch is not null ? new[] { repoRef.Branch } : ["main", "master"];
        string? zipPath = null;
        foreach (var branch in branches)
        {
            zipPath = await TryDownloadZipAsync(repoRef, branch, ct);
            if (zipPath is not null)
                break;
        }
        if (zipPath is null)
        {
            logger?.LogWarning(
                "Não foi possível baixar {Owner}/{Repo} (branches tentadas: {Branches}).",
                repoRef.Owner, repoRef.Repo, string.Join(", ", branches));
            return Result<FetchedTheme>.Fail(
                $"Não foi possível baixar {repoRef.Owner}/{repoRef.Repo} (branches tentadas: {string.Join(", ", branches)}).");
        }

        try
        {
            var dirName = $"{repoRef.Owner}-{repoRef.Repo}-{Guid.NewGuid():N}";
            var extractDir = Path.Combine(_cacheRoot, dirName.Length > 48 ? dirName[..48] : dirName);
            Directory.CreateDirectory(extractDir);
            // ExtractToDirectory do .NET valida entradas contra path traversal (zip slip).
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // O zipball do GitHub tem um único diretório-raiz "{repo}-{sha}".
            var repoDir = Directory.EnumerateDirectories(extractDir).SingleOrDefault()
                ?? extractDir;
            var themeDir = repoRef.SubPath.Length > 0
                ? Path.Combine(repoDir, repoRef.SubPath.Replace('/', Path.DirectorySeparatorChar))
                : repoDir;

            var manifestPath = Path.Combine(themeDir, ManifestFileName);
            if (!File.Exists(manifestPath))
                return Result<FetchedTheme>.Fail(
                    $"'{ManifestFileName}' não encontrado em {(repoRef.SubPath.Length > 0 ? repoRef.SubPath : "na raiz do repo")}.");

            var manifest = ManifestValidator.Parse(await File.ReadAllTextAsync(manifestPath, ct), logger);
            if (!manifest.IsSuccess)
                return Result<FetchedTheme>.Fail(manifest.Error!);

            logger?.LogInformation(
                "Tema '{ThemeId}' baixado de {Owner}/{Repo} para '{ThemeDir}'.",
                manifest.Value!.ThemeId, repoRef.Owner, repoRef.Repo, themeDir);
            return Result<FetchedTheme>.Ok(new FetchedTheme(themeDir, manifest.Value!));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>
    /// Manifesto local apontado diretamente (qualquer nome .json). A pasta que o contém
    /// vira a raiz do tema, então os `source` relativos continuam resolvendo igual.
    /// </summary>
    private async Task<Result<FetchedTheme>> ReadLocalManifestFileAsync(string manifestPath, CancellationToken ct)
    {
        if (!manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Result<FetchedTheme>.Fail("Um arquivo de tema local precisa ser um manifesto .json.");

        var manifest = ManifestValidator.Parse(await File.ReadAllTextAsync(manifestPath, ct), logger);
        if (!manifest.IsSuccess)
            return Result<FetchedTheme>.Fail(manifest.Error!);

        var themeDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidOperationException($"Não foi possível determinar a pasta de '{manifestPath}'.");

        return Result<FetchedTheme>.Ok(new FetchedTheme(themeDir, manifest.Value!));
    }

    private async Task<Result<FetchedTheme>> ReadLocalThemeAsync(string themeDir, CancellationToken ct)
    {
        var manifestPath = Path.Combine(themeDir, ManifestFileName);
        if (!File.Exists(manifestPath))
            return Result<FetchedTheme>.Fail($"'{ManifestFileName}' não encontrado em {themeDir}.");

        var manifest = ManifestValidator.Parse(await File.ReadAllTextAsync(manifestPath, ct), logger);
        return manifest.IsSuccess
            ? Result<FetchedTheme>.Ok(new FetchedTheme(Path.GetFullPath(themeDir), manifest.Value!))
            : Result<FetchedTheme>.Fail(manifest.Error!);
    }

    private async Task<string?> TryDownloadZipAsync(GitHubRepoRef repoRef, string branch, CancellationToken ct)
    {
        var url = $"https://codeload.github.com/{repoRef.Owner}/{repoRef.Repo}/zip/refs/heads/{branch}";
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        Directory.CreateDirectory(_cacheRoot);
        var zipPath = Path.Combine(_cacheRoot, $"download-{Guid.NewGuid():N}.zip");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(zipPath);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
            {
                await target.DisposeAsync();
                File.Delete(zipPath);
                throw new InvalidOperationException(
                    $"Tema excede o limite de {MaxDownloadBytes / (1024 * 1024)} MB.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return zipPath;
    }
}
