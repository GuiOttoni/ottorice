using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OttoRice.Common;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Abstração de execução de processo, para os clientes (WinGet, reloaders) serem testáveis.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct = default);

    /// <summary>Inicia um processo de longa duração (WM, barra) sem aguardar a saída.</summary>
    void StartDetached(string fileName, string arguments);

    /// <summary>PIDs vivos com esse nome de processo (sem extensão).</summary>
    IReadOnlyList<int> FindProcessIds(string processName);

    /// <summary>Encerra um PID específico. Nunca usar para matar processos em massa por nome.</summary>
    bool TryKill(int processId);
}

public sealed class ProcessRunner(ILogger<ProcessRunner>? logger = null) : IProcessRunner
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
        logger?.LogInformation("Processo iniciado (detached): '{FileName} {Arguments}'.", fileName, arguments);
    }

    public IReadOnlyList<int> FindProcessIds(string processName)
    {
        try
        {
            return [.. Process.GetProcessesByName(processName).Select(p => p.Id)];
        }
        catch (System.Exception ex)
        {
            logger?.LogWarning(ex, "Falha ao listar processos com nome '{ProcessName}'.", processName);
            return [];
        }
    }

    public bool TryKill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            return process.WaitForExit(5000);
        }
        catch (System.Exception ex)
        {
            logger?.LogWarning(ex, "Falha ao encerrar o processo pid {ProcessId}.", processId);
            return false;
        }
    }
}
