using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace OttoRice.Common;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Abstração de execução de processo, para os clientes (WinGet, reloaders) serem testáveis.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct = default);

    /// <summary>Inicia um processo de longa duração (WM, barra) sem aguardar a saída.</summary>
    void StartDetached(string fileName, string arguments);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new System.InvalidOperationException($"Não foi possível iniciar o processo '{fileName}'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public void StartDetached(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
