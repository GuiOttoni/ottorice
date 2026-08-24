using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace OttoRice.Common;

public interface IExecutableResolver
{
    /// <summary>Caminho absoluto do executável, ou null se não for encontrado em lugar nenhum.</summary>
    string? Resolve(string exeName);
}

/// <summary>
/// Localiza os CLIs das ferramentas de rice. Necessário porque duas coisas quebram o
/// Process.Start("glazewm") ingênuo, ambas observadas em instalação real:
///  1. o PATH é capturado quando o processo inicia — ferramentas instaladas pelo próprio
///     OttoRice na mesma sessão não aparecem nele;
///  2. GlazeWM, Zebar e YASB nem adicionam suas pastas ao PATH (instalam em Program Files).
/// Por isso a busca é: PATH do processo → PATH relido do registro → locais conhecidos.
/// </summary>
public sealed class ExecutableResolver : IExecutableResolver
{
    private static readonly IReadOnlyDictionary<string, string[]> KnownLocations =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["glazewm"] =
            [
                @"%ProgramFiles%\glzr.io\GlazeWM\cli\glazewm.exe",
                @"%ProgramFiles%\glzr.io\GlazeWM\glazewm.exe",
            ],
            ["zebar"] = [@"%ProgramFiles%\glzr.io\Zebar\zebar.exe"],
            ["yasbc"] = [@"%ProgramFiles%\YASB\yasbc.exe"],
            ["yasb"] = [@"%ProgramFiles%\YASB\yasb.exe"],
            ["windhawk-cli"] = [@"%ProgramFiles%\Windhawk\windhawk-cli.exe"],
            ["windhawk"] = [@"%ProgramFiles%\Windhawk\windhawk.exe"],
        };

    private readonly Func<string, string> _expand;
    private readonly Func<bool> _isWindows;
    private readonly ILogger<ExecutableResolver>? _logger;

    public ExecutableResolver(
        Func<string, string>? expand = null, Func<bool>? isWindows = null, ILogger<ExecutableResolver>? logger = null)
    {
        _expand = expand ?? Environment.ExpandEnvironmentVariables;
        _isWindows = isWindows ?? (() => OperatingSystem.IsWindows());
        _logger = logger;
    }

    public string? Resolve(string exeName)
    {
        var fileName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";

        var fromProcessPath = SearchDirectories(SplitPath(Environment.GetEnvironmentVariable("PATH")), fileName);
        if (fromProcessPath is not null)
            return fromProcessPath;

        var fromRefreshedPath = SearchDirectories(ReadPathFromRegistry(), fileName);
        if (fromRefreshedPath is not null)
            return fromRefreshedPath;

        if (KnownLocations.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var candidates))
        {
            foreach (var candidate in candidates)
            {
                var expanded = _expand(candidate);
                if (File.Exists(expanded))
                    return expanded;
            }
        }

        _logger?.LogWarning("'{ExeName}' não encontrado (PATH, registro nem locais conhecidos).", exeName);
        return null;
    }

    private static IEnumerable<string> SplitPath(string? path) =>
        (path ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string? SearchDirectories(IEnumerable<string> directories, string fileName)
    {
        foreach (var directory in directories)
        {
            string full;
            try
            {
                full = Path.Combine(_expand(directory), fileName);
            }
            catch (ArgumentException)
            {
                continue; // entrada de PATH malformada
            }

            if (File.Exists(full))
                return full;
        }
        return null;
    }

    /// <summary>PATH atual do sistema+usuário, relido do registro (pega instalações feitas após o app abrir).</summary>
    private IEnumerable<string> ReadPathFromRegistry()
    {
        if (!_isWindows())
            return [];

        var values = new List<string>();
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            values.AddRange(SplitPath(machine?.GetValue("Path") as string));

            using var user = Registry.CurrentUser.OpenSubKey("Environment");
            values.AddRange(SplitPath(user?.GetValue("Path") as string));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Sem acesso ao registro: seguimos com os locais conhecidos.
            _logger?.LogDebug(ex, "Sem acesso ao registro para reler o PATH — seguindo com locais conhecidos.");
        }
        return values;
    }
}
