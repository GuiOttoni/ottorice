using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OttoRice.Common;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeUninstall;

namespace OttoRice.Features.ThemeToggle;

/// <summary>
/// Aba "Temas instalados" (seção 12.3 do plano de evolução): lista todo tema instalado —
/// não só o ativo — com ações Ativar/Reaplicar/Desinstalar por tema. "Ativar" não inventa um
/// caminho de ativação novo: compõe <see cref="ThemeToggleService.TurnOffAsync"/> e
/// <see cref="ThemeToggleService.TurnOnAsync"/> via <see cref="ThemeToggleService.ActivateAsync"/>.
/// Desinstalar aqui não oferece a lista de ferramentas removíveis por checkbox (isso continua
/// na aba "Tema ativo", que é o fluxo detalhado) — decisão de escopo: aqui a desinstalação
/// remove só a entrada/configs do tema, mantendo as ferramentas WinGet instaladas.
/// </summary>
public partial class InstalledThemesViewModel(
    ThemeToggleService toggle,
    ReapplyThemeService reapply,
    UninstallService uninstall) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    public ObservableCollection<InstalledThemeItem> Themes { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var installed = await toggle.GetInstalledThemesAsync();
            Themes.Clear();
            foreach (var pair in installed.Themes.OrderByDescending(kv => kv.Value.InstalledAt))
                Themes.Add(new InstalledThemeItem(pair.Key, pair.Value, pair.Key == installed.ActiveThemeId));
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task ActivateAsync(InstalledThemeItem? item) => item is null
        ? Task.CompletedTask
        : RunAsync(progress => toggle.ActivateAsync(item.ThemeId, progress), $"✅ '{item.ThemeName}' ativado.");

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task ReapplyAsync(InstalledThemeItem? item) => item is null
        ? Task.CompletedTask
        : RunAsync(progress => reapply.ReapplyAsync(item.ThemeId, progress), $"✅ '{item.ThemeName}' reaplicado.");

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task UninstallAsync(InstalledThemeItem? item) => item is null
        ? Task.CompletedTask
        : RunAsync(
            progress => uninstall.UninstallAsync(item.ThemeId, null, progress),
            $"✅ '{item.ThemeName}' desinstalado.");

    private async Task RunAsync(Func<Action<string>, Task<Result>> operation, string successMessage)
    {
        IsBusy = true;
        Log.Clear();
        try
        {
            var result = await operation(Log.Add);
            StatusMessage = result.IsSuccess ? successMessage : $"❌ {result.Error}";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Falha na operação sobre um tema instalado");
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }
}

/// <summary>Linha da lista "Temas instalados" — projeção somente leitura de uma entrada de <see cref="ThemeState"/>.</summary>
public sealed class InstalledThemeItem(string themeId, ThemeState state, bool isActive)
{
    public string ThemeId { get; } = themeId;
    public string ThemeName { get; } = state.ThemeName ?? themeId;
    public bool IsActive { get; } = isActive;
    public bool IsEnabled { get; } = state.IsEnabled;
    public DateTimeOffset InstalledAt { get; } = state.InstalledAt;

    public string StatusLabel => !IsActive
        ? "Instalado (inativo)"
        : IsEnabled ? "🟢 Ativo e ligado" : "⚪ Ativo e desligado";
}
