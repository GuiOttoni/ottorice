using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OttoRice.Common;

public interface ITaskbarService
{
    /// <summary>Estado atual de auto-hide, ou null se a taskbar não pôde ser localizada.</summary>
    bool? GetAutoHide();

    /// <summary>
    /// Liga/desliga "ocultar automaticamente a barra de tarefas" — o mesmo mecanismo
    /// nativo de Configurações &gt; Personalização &gt; Barra de tarefas. Nunca lança:
    /// se a janela da taskbar não for encontrada (estado transitório de logon/explorer.exe
    /// reiniciando), é um no-op registrado em log.
    /// </summary>
    void SetAutoHide(bool enabled);
}

/// <summary>
/// Esconde/restaura a taskbar nativa via SHAppBarMessage sobre a janela Shell_TrayWnd —
/// o mesmo caminho que o próprio Explorer usa para o toggle de auto-hide em
/// Configurações &gt; Personalização &gt; Barra de tarefas, sem depender de ferramenta de
/// terceiros (a tentativa anterior com TranslucentTB foi descartada: é distribuído como
/// pacote MSIX com config em ApplicationData binário, não portable com settings.json — ver
/// doc OttoContext seção 10) nem estender a whitelist de apps do manifesto. Só é acionado
/// pelo OttoRice quando o GlazeWM está entre os apps geridos pelo tema (GlazeWM assume o
/// papel de barra/gerenciador de janelas).
/// </summary>
public sealed partial class TaskbarService(ILogger<TaskbarService>? logger = null) : ITaskbarService
{
    private const int AbmGetState = 0x00000004;
    private const int AbmSetState = 0x0000000A;
    private const int AbsAutoHide = 0x0000001;
    private const int AbsAlwaysOnTop = 0x0000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string lpClassName, nint lpWindowName);

    [LibraryImport("shell32.dll")]
    private static partial nint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public bool? GetAutoHide()
    {
        var hwnd = FindTrayWindow();
        if (hwnd == nint.Zero)
            return null;

        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        var state = (int)SHAppBarMessage(AbmGetState, ref data);
        return (state & AbsAutoHide) != 0;
    }

    public void SetAutoHide(bool enabled)
    {
        var hwnd = FindTrayWindow();
        if (hwnd == nint.Zero)
        {
            logger?.LogWarning("Barra de tarefas não encontrada — auto-hide não aplicado.");
            return;
        }

        var data = new APPBARDATA
        {
            cbSize = Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            lParam = enabled ? AbsAutoHide : AbsAlwaysOnTop,
        };
        SHAppBarMessage(AbmSetState, ref data);
        logger?.LogInformation("Auto-hide da barra de tarefas definido para {Enabled}.", enabled);
    }

    private static nint FindTrayWindow() => FindWindow("Shell_TrayWnd", nint.Zero);
}
