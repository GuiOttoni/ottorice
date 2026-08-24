using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using OttoRice.AppRegistry.Appliers;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;
using System.Net.Http;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;
using OttoRice.Features.ThemeToggle;
using OttoRice.Features.ThemeUninstall;
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
        // O logger é configurado em Program.Main, antes da Avalonia subir.
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IExecutableResolver>(_ => new ExecutableResolver());
        services.AddSingleton<IWinGetClient, WinGetClient>();
        services.AddSingleton<IWallpaperService, WindowsWallpaperService>();
        services.AddSingleton<IAppReloader, AppReloader>();
        services.AddSingleton<WindowsTerminalLocator>();
        services.AddSingleton<FileOverrideApplier>();
        services.AddSingleton<WindowsTerminalApplier>();
        services.AddSingleton<BackupSessionStore>();
        services.AddSingleton<InstallHistoryStore>();
        services.AddSingleton<TargetPlanner>();

        services.AddSingleton(_ =>
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OttoRice/0.1");
            return http;
        });
        services.AddSingleton<IThemeFetcher>(sp => new ThemeFetcher(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IThemeFilePicker>(_ =>
            new AvaloniaThemeFilePicker(AvaloniaThemeFilePicker.CurrentMainWindow));

        services.AddSingleton<ThemeStateStore>();
        services.AddSingleton<ThemeToggleService>();
        services.AddSingleton<UninstallService>();

        services.AddTransient<InstallViewModel>();
        services.AddTransient<ThemeControlViewModel>();
        services.AddTransient<BackupsViewModel>();
        services.AddTransient<MainViewModel>();

        services.AddTransient<InstallPipeline>(sp => new InstallPipeline(
        [
            new PlanStep(sp.GetRequiredService<TargetPlanner>()),
            new DependencyStep(sp.GetRequiredService<IWinGetClient>()),
            new BackupStep(sp.GetRequiredService<BackupSessionStore>(), sp.GetRequiredService<IWallpaperService>()),
            new ApplyStep(
                sp.GetRequiredService<FileOverrideApplier>(),
                sp.GetRequiredService<WindowsTerminalApplier>(),
                sp.GetRequiredService<IWallpaperService>()),
            new ReloadStep(sp.GetRequiredService<IAppReloader>()),
        ]));

        return services.BuildServiceProvider();
    }
}
