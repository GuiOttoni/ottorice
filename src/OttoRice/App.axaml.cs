using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry.Appliers;
using OttoRice.AppRegistry.Reloaders;
using OttoRice.Common;
using System.Net.Http;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeEditor;
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

        // Ponte pro Serilog já configurado em Program.Main: os serviços recebem
        // ILogger<T> por DI em vez de usar Serilog.Log estático.
        services.AddLogging(b => b.AddSerilog(dispose: false));

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IExecutableResolver>(
            sp => new ExecutableResolver(logger: sp.GetService<ILogger<ExecutableResolver>>()));
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
        services.AddSingleton<IThemeFetcher>(sp => new ThemeFetcher(
            sp.GetRequiredService<HttpClient>(), logger: sp.GetService<ILogger<ThemeFetcher>>()));
        services.AddSingleton<IThemeFilePicker>(sp =>
            new AvaloniaThemeFilePicker(
                AvaloniaThemeFilePicker.CurrentMainWindow, sp.GetService<ILogger<AvaloniaThemeFilePicker>>()));

        services.AddSingleton<ThemeStateStore>();
        services.AddSingleton<ThemeToggleService>();
        services.AddSingleton<UninstallService>();

        services.AddTransient<InstallViewModel>();
        services.AddTransient<ThemeControlViewModel>();
        services.AddTransient<InstalledThemesViewModel>();
        services.AddTransient<BackupsViewModel>();
        services.AddTransient<ThemeEditorViewModel>();
        services.AddTransient<MainViewModel>();

        services.AddTransient<InstallPipeline>(sp => new InstallPipeline(
        [
            // Dependências antes do Planejamento: instalar via WinGet primeiro evita que o
            // Planejamento precise adivinhar sobre ferramentas ainda não presentes na máquina.
            new DependencyStep(sp.GetRequiredService<IWinGetClient>(), sp.GetService<ILogger<DependencyStep>>()),
            new PlanStep(sp.GetRequiredService<TargetPlanner>(), sp.GetService<ILogger<PlanStep>>()),
            new BackupStep(
                sp.GetRequiredService<BackupSessionStore>(),
                sp.GetRequiredService<IWallpaperService>(),
                sp.GetService<ILogger<BackupStep>>()),
            new ApplyStep(
                sp.GetRequiredService<FileOverrideApplier>(),
                sp.GetRequiredService<WindowsTerminalApplier>(),
                sp.GetRequiredService<IWallpaperService>(),
                sp.GetService<ILogger<ApplyStep>>()),
            new ReloadStep(
                sp.GetRequiredService<IAppReloader>(),
                sp.GetRequiredService<IProcessRunner>(),
                sp.GetService<ILogger<ReloadStep>>()),
            new ConfigureWindhawkModsStep(
                sp.GetRequiredService<IExecutableResolver>(),
                sp.GetRequiredService<IProcessRunner>(),
                sp.GetService<ILogger<ConfigureWindhawkModsStep>>()),
        ], sp.GetService<ILogger<InstallPipeline>>()));

        // Pipeline reduzida para "Reaplicar tema" (seção 12.2 do plano de evolução):
        // Planejamento → Aplicação → Reload → Mods do Windhawk, sem Dependência (já
        // instaladas) nem Backup (evita poluir BackupSessionStore/InstallHistoryStore com
        // sessões duplicadas do mesmo tema). Instância própria (não reaproveita a
        // InstallPipeline "cheia" registrada acima) para não haver ambiguidade no DI.
        services.AddTransient<ReapplyThemeService>(sp => new ReapplyThemeService(
            sp.GetRequiredService<IThemeFetcher>(),
            new InstallPipeline(
            [
                new PlanStep(sp.GetRequiredService<TargetPlanner>(), sp.GetService<ILogger<PlanStep>>()),
                new ApplyStep(
                    sp.GetRequiredService<FileOverrideApplier>(),
                    sp.GetRequiredService<WindowsTerminalApplier>(),
                    sp.GetRequiredService<IWallpaperService>(),
                    sp.GetService<ILogger<ApplyStep>>()),
                new ReloadStep(
                    sp.GetRequiredService<IAppReloader>(),
                    sp.GetRequiredService<IProcessRunner>(),
                    sp.GetService<ILogger<ReloadStep>>()),
                new ConfigureWindhawkModsStep(
                    sp.GetRequiredService<IExecutableResolver>(),
                    sp.GetRequiredService<IProcessRunner>(),
                    sp.GetService<ILogger<ConfigureWindhawkModsStep>>()),
            ], sp.GetService<ILogger<InstallPipeline>>()),
            sp.GetRequiredService<ThemeStateStore>(),
            sp.GetService<ILogger<ReapplyThemeService>>()));

        return services.BuildServiceProvider();
    }
}
