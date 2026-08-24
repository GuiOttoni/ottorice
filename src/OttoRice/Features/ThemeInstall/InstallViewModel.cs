using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OttoRice.AppRegistry;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeToggle;
using Serilog;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Fluxo do MVP: colar URL → Buscar (preview + consentimento informado, RF-03) →
/// Instalar (pipeline transacional com progresso e cancelamento) → concluído/rollback.
/// </summary>
public partial class InstallViewModel(
    IThemeFetcher fetcher,
    InstallPipeline pipeline,
    InstallHistoryStore history,
    ThemeStateStore stateStore,
    IThemeFilePicker filePicker) : ObservableObject
{
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    private string _themeUrl = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Cole a URL de um repositório GitHub com um rice-manifest.json.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private FetchedTheme? _fetchedTheme;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _previewImage;

    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<string> AffectedApps { get; } = [];
    public ObservableCollection<string> Dependencies { get; } = [];
    public ObservableCollection<StepStatusItem> Steps { get; } = [];

    public bool HasPreview => FetchedTheme is not null;
    public string ThemeTitle => FetchedTheme is null
        ? ""
        : $"{FetchedTheme.Manifest.Name} — por {FetchedTheme.Manifest.Author ?? "desconhecido"}";

    private bool NotBusy() => !IsBusy;

    /// <summary>Escolhe um manifesto no disco e já busca o tema.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task BrowseAsync()
    {
        var manifestPath = await filePicker.PickManifestAsync();
        if (manifestPath is null)
            return;

        ThemeUrl = manifestPath;
        await FetchAsync();
    }

    private bool CanFetch() => !IsBusy && !string.IsNullOrWhiteSpace(ThemeUrl);

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private async Task FetchAsync()
    {
        IsBusy = true;
        FetchedTheme = null;
        PreviewImage?.Dispose();
        PreviewImage = null;
        Log.Clear();
        StatusMessage = "Baixando o tema do GitHub...";

        try
        {
            var result = await fetcher.FetchAsync(ThemeUrl);
            if (!result.IsSuccess)
            {
                StatusMessage = $"❌ {result.Error}";
                return;
            }

            FetchedTheme = result.Value;
            var manifest = FetchedTheme!.Manifest;

            AffectedApps.Clear();
            foreach (var app in manifest.Targets
                         .Select(t => SupportedApps.All[t.App!].DisplayName).Distinct())
                AffectedApps.Add(app);

            Dependencies.Clear();
            foreach (var dep in manifest.Dependencies)
                Dependencies.Add(dep.WingetId!);

            Steps.Clear();
            foreach (var name in pipeline.StepNames)
                Steps.Add(new StepStatusItem(name));

            if (manifest.Preview is not null)
            {
                var previewPath = Path.Combine(FetchedTheme.ThemeDirectory, manifest.Preview);
                if (File.Exists(previewPath))
                {
                    try
                    {
                        PreviewImage = new Avalonia.Media.Imaging.Bitmap(previewPath);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "Preview do tema ilegível — seguindo sem imagem");
                    }
                }
            }

            OnPropertyChanged(nameof(ThemeTitle));
            StatusMessage = "Revise o que será alterado e clique em Instalar.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Falha ao buscar tema");
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstall() => !IsBusy && FetchedTheme is not null;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var theme = FetchedTheme!;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Log.Clear();
        StatusMessage = $"Instalando '{theme.Manifest.Name}'...";

        Steps.Clear();
        foreach (var name in pipeline.StepNames)
            Steps.Add(new StepStatusItem(name));

        try
        {
            var context = new InstallContext
            {
                Manifest = theme.Manifest,
                ThemeDirectory = theme.ThemeDirectory,
                Progress = Log.Add, // steps continuam no contexto da UI (sem ConfigureAwait(false))
                StepStateChanged = (name, state) =>
                {
                    var item = Steps.FirstOrDefault(s => s.Name == name);
                    if (item is not null)
                        item.State = state;
                },
            };

            var result = await pipeline.RunAsync(context, _cts.Token);
            if (result.IsSuccess)
            {
                await history.AppendAsync(new InstallRecord(
                    theme.Manifest.ThemeId!,
                    theme.Manifest.Name!,
                    context.BackupSession?.Id ?? "",
                    DateTimeOffset.Now,
                    [.. context.WingetIdsInstalled]));
                await SaveThemeStateAsync(context);
                StatusMessage = "✅ Tema aplicado com sucesso!";
            }
            else
            {
                StatusMessage = $"❌ {result.Error} (alterações desfeitas)";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Falha inesperada na instalação");
            StatusMessage = $"❌ Erro inesperado: {ex.Message}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private bool CanCancel() => IsBusy && _cts is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Registra o que o toggle (RF-15) precisa saber para ligar/desligar este tema depois.</summary>
    private async Task SaveThemeStateAsync(InstallContext context)
    {
        var wallpaperOp = context.Operations.FirstOrDefault(op => op.Target.Action == "set");
        var glazeOp = context.Operations.FirstOrDefault(op => op.Target.App == "glazewm");

        await stateStore.WriteAsync(new ThemeState
        {
            ActiveThemeId = context.Manifest.ThemeId,
            ActiveThemeName = context.Manifest.Name,
            IsEnabled = true,
            SourceUrl = ThemeUrl,
            OriginalWallpaperPath = context.PreviousWallpaperPath,
            OriginalWallpaperCopy = await stateStore.PreserveWallpaperAsync(context.PreviousWallpaperPath),
            ThemeWallpaperPath = wallpaperOp?.SourcePath,
            GlazeWmConfigPath = glazeOp?.TargetPath,
            ManagedApps = [.. context.Operations.Select(op => op.Target.App!).Distinct()],
        });
    }
}
