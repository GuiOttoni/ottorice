using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OttoRice.AppRegistry.Appliers;
using OttoRice.Common;
using Serilog;

namespace OttoRice;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ConfigureLogging();
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
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

        Log.Information("OttoRice iniciado");
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<WinGetClient>();
        services.AddSingleton<WindowsTerminalLocator>();
        services.AddSingleton<FileOverrideApplier>();
        services.AddSingleton<WindowsTerminalApplier>();

        return services.BuildServiceProvider();
    }
}
