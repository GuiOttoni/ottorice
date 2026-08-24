using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OttoRice.Common;

public interface IWallpaperService
{
    /// <summary>Caminho do wallpaper atual, para permitir restauração no rollback/toggle.</summary>
    string? GetCurrentPath();

    void Set(string imagePath);
}

public sealed partial class WindowsWallpaperService : IWallpaperService
{
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, string buffer, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoGet(uint action, uint param, [Out] char[] buffer, uint winIni);

    public string? GetCurrentPath()
    {
        var buffer = new char[512];
        if (!SystemParametersInfoGet(SpiGetDeskWallpaper, (uint)buffer.Length, buffer, 0))
            return null;

        var path = new string(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public void Set(string imagePath)
    {
        var fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Imagem de wallpaper não encontrada: {fullPath}");

        if (!SystemParametersInfo(SpiSetDeskWallpaper, 0, fullPath, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException(
                $"SystemParametersInfo falhou ao definir o wallpaper (erro {Marshal.GetLastPInvokeError()}).");
    }
}
