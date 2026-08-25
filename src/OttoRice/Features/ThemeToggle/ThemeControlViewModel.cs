using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeUninstall;

namespace OttoRice.Features.ThemeToggle;

/// <summary>
/// Aba "Tema ativo": liga/desliga (RF-15), pausa o tiling, reaplica (seção 12.2 do plano de
/// evolução) e desinstala (RF-16). A remoção de ferramentas é opt-in e só habilitada para as
/// com refcount zero.
/// </summary>
public partial class ThemeControlViewModel(
    ThemeToggleService toggle,
    UninstallService uninstall,
    ReapplyThemeService reapply) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand))]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeTitle))]
    [NotifyPropertyChangedFor(nameof(HasActiveTheme))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyCanExecuteChangedFor(nameof(TurnOnCommand))]
    [NotifyCanExecuteChangedFor(nameof(TurnOffCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReapplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(UninstallCommand))]
    private InstalledThemes _installed = InstalledThemes.Empty;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>True entre o primeiro clique em "Desinstalar tema" e o segundo (confirmação) —
    /// desinstalar é destrutivo (some com a config do tema, mesmo restaurando o backup) e não
    /// tinha nenhuma confirmação; segue o mesmo padrão de dois cliques já usado em Reapply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UninstallButtonLabel))]
    private bool _isConfirmingUninstall;

    public string UninstallButtonLabel => IsConfirmingUninstall ? "CONFIRMAR DESINSTALAÇÃO" : "DESINSTALAR TEMA";

    /// <summary>Ferramentas do tema marcadas para remoção junto com ele.</summary>
    public ObservableCollection<RemovableToolViewModel> RemovableTools { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public bool HasActiveTheme => Installed.HasActiveTheme;
    public string ThemeTitle => Installed.ActiveTheme?.ThemeName ?? "Nenhum tema aplicado pelo OttoRice.";
    public string StateLabel => Installed.ActiveTheme is not { } active ? "" : active.IsEnabled ? "🟢 Ligado" : "⚪ Desligado";

    partial void OnInstalledChanged(InstalledThemes value) => IsConfirmingUninstall = false;

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Installed = await toggle.GetInstalledThemesAsync();
            RemovableTools.Clear();
            if (Installed.ActiveThemeId is not null)
            {
                foreach (var tool in await uninstall.GetRemovableToolsAsync(Installed.ActiveThemeId))
                    RemovableTools.Add(new RemovableToolViewModel(tool));
            }
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

    private bool CanTurnOn() => !IsBusy && Installed.ActiveTheme is { IsEnabled: false };

    [RelayCommand(CanExecute = nameof(CanTurnOn))]
    private Task TurnOnAsync() => RunAsync(
        progress => toggle.TurnOnAsync(Installed.ActiveThemeId, progress), "✅ Tema ligado.");

    private bool CanTurnOff() => !IsBusy && Installed.ActiveTheme is { IsEnabled: true };

    [RelayCommand(CanExecute = nameof(CanTurnOff))]
    private Task TurnOffAsync() => RunAsync(
        progress => toggle.TurnOffAsync(Installed.ActiveThemeId, progress),
        "✅ Tema desligado. As janelas voltaram a ficar visíveis, mas o GlazeWM não restaura as posições originais.");

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task TogglePauseAsync()
    {
        IsBusy = true;
        try
        {
            var result = await toggle.TogglePauseAsync();
            StatusMessage = result.IsSuccess
                ? "✅ Tiling pausado/retomado (GlazeWM continua rodando)."
                : $"❌ {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Permitido mesmo com o tema desligado (sobrescreve os arquivos de qualquer forma) — a
    // mensagem de sucesso avisa quando o tema está desligado, ver ReapplyAsync.
    private bool CanReapply() => !IsBusy && Installed.HasActiveTheme;

    [RelayCommand(CanExecute = nameof(CanReapply))]
    private Task ReapplyAsync()
    {
        var themeId = Installed.ActiveThemeId!;
        var wasEnabled = Installed.ActiveTheme!.IsEnabled;
        return RunAsync(
            progress => reapply.ReapplyAsync(themeId, progress),
            wasEnabled
                ? "✅ Tema reaplicado."
                : "✅ Configurações reaplicadas (o tema está desligado — ligue para ver o efeito).");
    }

    private bool CanUninstall() => !IsBusy && Installed.HasActiveTheme;

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private async Task UninstallAsync()
    {
        // Primeiro clique só arma a confirmação (some com a config do tema, mesmo restaurando
        // o backup) — o segundo clique, com o botão já dizendo "CONFIRMAR...", executa de fato.
        if (!IsConfirmingUninstall)
        {
            IsConfirmingUninstall = true;
            return;
        }

        IsConfirmingUninstall = false;
        var themeId = Installed.ActiveThemeId!;
        var selected = RemovableTools.Where(t => t.RemoveIt).Select(t => t.WingetId).ToArray();

        await RunAsync(
            progress => uninstall.UninstallAsync(themeId, selected, progress),
            "✅ Tema desinstalado e configurações anteriores restauradas.");
        await RefreshAsync();
    }

    [RelayCommand]
    private void CancelUninstall() => IsConfirmingUninstall = false;

    private async Task RunAsync(
        Func<Action<string>, Task<Common.Result>> operation, string successMessage)
    {
        IsBusy = true;
        Log.Clear();
        try
        {
            var result = await operation(Log.Add);
            StatusMessage = result.IsSuccess ? successMessage : $"❌ {result.Error}";
            Installed = await toggle.GetInstalledThemesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Falha na operação de tema");
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class RemovableToolViewModel(RemovableTool tool) : ObservableObject
{
    [ObservableProperty]
    private bool _removeIt;

    public string WingetId => tool.WingetId;
    public bool CanRemove => tool.IsSafeToRemove;
    public string Description => tool.IsSafeToRemove
        ? $"{tool.WingetId} — nenhum outro tema usa"
        : $"{tool.WingetId} — usado por {tool.OtherThemesUsing} outro(s) tema(s), será mantido";
}
