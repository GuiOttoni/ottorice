using System;
using System.IO;
using System.Linq;

namespace OttoRice.Common;

public static class PathResolver
{
    public static string Expand(string path) =>
        Environment.ExpandEnvironmentVariables(path);
}

/// <summary>
/// Localiza o settings.json do Windows Terminal: primeiro a versão da Store
/// (pacote com sufixo variável), depois a instalação unpackaged.
/// </summary>
public sealed class WindowsTerminalLocator(string? localAppDataOverride = null)
{
    private readonly string _localAppData = localAppDataOverride
        ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string? FindSettingsPath()
    {
        var packagesDir = Path.Combine(_localAppData, "Packages");
        if (Directory.Exists(packagesDir))
        {
            var candidate = Directory
                .EnumerateDirectories(packagesDir, "Microsoft.WindowsTerminal_*")
                .Select(dir => Path.Combine(dir, "LocalState", "settings.json"))
                .FirstOrDefault(File.Exists);
            if (candidate is not null)
                return candidate;
        }

        var unpackaged = Path.Combine(_localAppData, "Microsoft", "Windows Terminal", "settings.json");
        return File.Exists(unpackaged) ? unpackaged : null;
    }
}
