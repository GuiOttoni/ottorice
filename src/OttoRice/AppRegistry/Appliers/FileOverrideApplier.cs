using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Appliers;

/// <summary>Override simples (GlazeWM, YASB): cópia atômica do arquivo do tema sobre o alvo.</summary>
public sealed class FileOverrideApplier(ILogger<FileOverrideApplier>? logger = null) : IConfigApplier
{
    public async Task ApplyAsync(string sourcePath, string targetPath, CancellationToken ct = default)
    {
        await AtomicFileWriter.CopyAsync(sourcePath, targetPath, ct, logger);
        logger?.LogInformation("Override aplicado: '{SourcePath}' -> '{TargetPath}'.", sourcePath, targetPath);
    }
}
