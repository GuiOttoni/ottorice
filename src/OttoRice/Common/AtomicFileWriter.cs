using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OttoRice.Common;

/// <summary>
/// Escrita atômica: grava em um .tmp no mesmo diretório e substitui via File.Move,
/// delegando a atomicidade ao NTFS. Nunca deixa o arquivo alvo pela metade.
/// </summary>
public static class AtomicFileWriter
{
    public static async Task WriteAllTextAsync(
        string targetPath, string content, CancellationToken ct = default, ILogger? logger = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, ct);
        File.Move(tempPath, targetPath, overwrite: true);
        logger?.LogDebug("Escrita atômica concluída em '{TargetPath}'.", targetPath);
    }

    public static async Task CopyAsync(
        string sourcePath, string targetPath, CancellationToken ct = default, ILogger? logger = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(tempPath))
        {
            await source.CopyToAsync(destination, ct);
        }
        File.Move(tempPath, targetPath, overwrite: true);
        logger?.LogDebug("Cópia atômica de '{SourcePath}' para '{TargetPath}' concluída.", sourcePath, targetPath);
    }
}
