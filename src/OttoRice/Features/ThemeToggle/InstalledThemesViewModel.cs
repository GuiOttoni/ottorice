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

    /// <summary>
    /// Reaplicar é em dois cliques: o primeiro busca os componentes do tema (RiceTarget[])
    /// pra deixar o usuário ligar/desligar cada um (toggle por componente — "configurar um
    /// tema" também cobre reaplicação, não só a instalação inicial); o segundo confirma com
    /// a seleção feita. <see cref="InstalledThemeItem.IsSelectingTargets"/> distingue os dois.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ReapplyAsync(InstalledThemeItem? item)
    {
        if (item is null)
            return;

        if (!item.IsSelectingTargets)
        {
            await PrepareReapplySelectionAsync(item);
            return;
        }

        var selected = item.Targets.Where(t => t.IsSelected).Select(t => t.Index).ToHashSet();
        item.IsSelectingTargets = false;
        await RunAsync(
            progress => reapply.ReapplyAsync(item.ThemeId, progress, selectedTargetIndexes: selected),
            $"✅ '{item.ThemeName}' reaplicado.");
    }

    private async Task PrepareReapplySelectionAsync(InstalledThemeItem item)
    {
        IsBusy = true;
        try
        {
            var targets = await reapply.FetchTargetsAsync(item.ThemeId);
            if (!targets.IsSuccess)
            {
                StatusMessage = $"❌ {targets.Error}";
                return;
            }

            item.Targets.Clear();
            for (var i = 0; i < targets.Value!.Count; i++)
                item.Targets.Add(new TargetSelectionItem(i, targets.Value[i]));
            item.IsSelectingTargets = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Desiste da seleção de componentes aberta por <see cref="ReapplyAsync"/> sem reaplicar nada.</summary>
    [RelayCommand]
    private void CancelReapplySelection(InstalledThemeItem? item)
    {
        if (item is null)
            return;
        item.IsSelectingTargets = false;
        item.Targets.Clear();
    }

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

/// <summary>Linha da lista "Temas instalados" — projeção de uma entrada de <see cref="ThemeState"/>,
/// mais o estado (não persistido — ver <see cref="TargetSelectionItem"/>) da seleção de
/// componentes pra reaplicar em dois cliques.</summary>
public sealed partial class InstalledThemeItem(string themeId, ThemeState state, bool isActive) : ObservableObject
{
    public string ThemeId { get; } = themeId;
    public string ThemeName { get; } = state.ThemeName ?? themeId;
    public bool IsActive { get; } = isActive;
    public bool IsEnabled { get; } = state.IsEnabled;
    public DateTimeOffset InstalledAt { get; } = state.InstalledAt;

    public string StatusLabel => !IsActive
        ? "Instalado (inativo)"
        : IsEnabled ? "🟢 Ativo e ligado" : "⚪ Ativo e desligado";

    /// <summary>Componentes do tema (buscados sob demanda no primeiro clique em "Reaplicar"),
    /// pra ligar/desligar antes de confirmar. Vazio até então.</summary>
    public ObservableCollection<TargetSelectionItem> Targets { get; } = [];

    /// <summary>True entre o primeiro clique em "Reaplicar" (busca os componentes) e o
    /// segundo (confirma a seleção) — troca o botão por "Confirmar" + a lista de checkboxes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReapplyButtonLabel))]
    private bool _isSelectingTargets;

    public string ReapplyButtonLabel => IsSelectingTargets ? "CONFIRMAR REAPLICAÇÃO" : "REAPLICAR";
}
