using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Appliers;

/// <summary>Override simples (GlazeWM, YASB): cópia atômica do arquivo do tema sobre o alvo.</summary>
public sealed class FileOverrideApplier : IConfigApplier
{
    public Task ApplyAsync(string sourcePath, string targetPath, CancellationToken ct = default) =>
        AtomicFileWriter.CopyAsync(sourcePath, targetPath, ct);
}
