using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Serilog;

namespace OttoRice;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Logger criado antes da Avalonia: se o processo morrer por exceção fora do
        // dispatcher de UI, o motivo fica registrado em disco em vez de sumir.
        ConfigureLogging();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error(e.ExceptionObject as Exception,
                "Unhandled exception (AppDomain) — processo provavelmente vai encerrar");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        Log.Information("OttoRice iniciando...");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exceção fatal ao iniciar/rodar a aplicação");
            throw;
        }
        finally
        {
            Log.Information("OttoRice encerrado.");
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OttoRice", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(logDir, "ottorice-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
