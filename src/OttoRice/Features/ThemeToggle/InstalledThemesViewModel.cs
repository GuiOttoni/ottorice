using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;
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
    [NotifyCanExecuteChangedFor(nameof(SelectPaletteCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyPaletteCommand))]
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

    /// <summary>
    /// Abre o seletor de paleta (seção 13 da doc "OttoRice"): busca de novo o manifesto do
    /// tema pra listar <c>palettes[]</c>. Tema sem paletas alternativas mostra um aviso em vez
    /// de abrir a lista — não há botão separado "trocar paleta" só pra temas que a declaram, o
    /// próprio clique já revela isso (evita buscar o manifesto de todo tema listado a cada
    /// ATUALIZAR só pra saber se ele tem paletas).
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task SelectPaletteAsync(InstalledThemeItem? item)
    {
        if (item is null)
            return;

        IsBusy = true;
        try
        {
            var palettes = await reapply.FetchPalettesAsync(item.ThemeId);
            if (!palettes.IsSuccess)
            {
                StatusMessage = $"❌ {palettes.Error}";
                return;
            }

            if (palettes.Value!.Count == 0)
            {
                StatusMessage = $"'{item.ThemeName}' não declara paletas de cores alternativas.";
                return;
            }

            item.Palettes.Clear();
            item.Palettes.Add(new PaletteOption(null, "Padrão"));
            foreach (var palette in palettes.Value)
                item.Palettes.Add(new PaletteOption(palette.Id, palette.Name ?? palette.Id ?? "?"));

            item.SelectedPalette = item.Palettes.FirstOrDefault(p => p.Id == item.ActivePaletteId)
                ?? item.Palettes[0];
            item.IsSelectingPalette = true;
            StatusMessage = "";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Confirma a paleta escolhida em <see cref="SelectPaletteAsync"/> e reaplica o tema com ela.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ApplyPaletteAsync(InstalledThemeItem? item)
    {
        if (item is null || item.SelectedPalette is null)
            return;

        var paletteId = item.SelectedPalette.Id;
        item.IsSelectingPalette = false;
        await RunAsync(
            progress => reapply.ApplyPaletteAsync(item.ThemeId, paletteId, progress),
            $"✅ Paleta '{item.SelectedPalette.Name}' aplicada a '{item.ThemeName}'.");
    }

    /// <summary>Desiste da seleção de paleta aberta por <see cref="SelectPaletteAsync"/> sem aplicar nada.</summary>
    [RelayCommand]
    private void CancelPaletteSelection(InstalledThemeItem? item)
    {
        if (item is null)
            return;
        item.IsSelectingPalette = false;
        item.Palettes.Clear();
    }

    /// <summary>Desinstalar é destrutivo (some com a config do tema) e não tinha nenhuma
    /// confirmação — primeiro clique só arma (troca o rótulo do botão pra "CONFIRMAR" e mostra
    /// "CANCELAR"), segundo clique executa. Mesmo padrão de dois cliques do Reapply acima.</summary>
    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task UninstallAsync(InstalledThemeItem? item)
    {
        if (item is null)
            return Task.CompletedTask;

        if (!item.IsConfirmingUninstall)
        {
            item.IsConfirmingUninstall = true;
            return Task.CompletedTask;
        }

        item.IsConfirmingUninstall = false;
        return RunAsync(
            progress => uninstall.UninstallAsync(item.ThemeId, null, progress),
            $"✅ '{item.ThemeName}' desinstalado.");
    }

    /// <summary>Desiste da confirmação de desinstalação armada por <see cref="UninstallAsync"/>.</summary>
    [RelayCommand]
    private void CancelUninstall(InstalledThemeItem? item)
    {
        if (item is not null)
            item.IsConfirmingUninstall = false;
    }

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

    /// <summary>Id da paleta ativa no momento (seção 13 da doc "OttoRice"), ou <c>null</c> = padrão.</summary>
    public string? ActivePaletteId { get; } = state.ActivePaletteId;

    public string PaletteLabel => ActivePaletteId is null ? "Paleta: padrão" : $"Paleta: {ActivePaletteId}";

    public string StatusLabel => !IsActive
        ? "Instalado (inativo)"
        : IsEnabled ? "🟢 Ativo e ligado" : "⚪ Ativo e desligado";

    /// <summary>Opções de paleta (buscadas sob demanda no primeiro clique em "PALETA"), pra
    /// escolher antes de confirmar. Vazio até então.</summary>
    public ObservableCollection<PaletteOption> Palettes { get; } = [];

    [ObservableProperty]
    private PaletteOption? _selectedPalette;

    /// <summary>True entre o primeiro clique em "PALETA" (busca as opções) e o segundo
    /// (confirma a escolha) — troca o botão por "APLICAR" + o seletor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaletteButtonLabel))]
    private bool _isSelectingPalette;

    public string PaletteButtonLabel => IsSelectingPalette ? "CONFIRMAR PALETA" : "PALETA";

    /// <summary>Componentes do tema (buscados sob demanda no primeiro clique em "Reaplicar"),
    /// pra ligar/desligar antes de confirmar. Vazio até então.</summary>
    public ObservableCollection<TargetSelectionItem> Targets { get; } = [];

    /// <summary>True entre o primeiro clique em "Reaplicar" (busca os componentes) e o
    /// segundo (confirma a seleção) — troca o botão por "Confirmar" + a lista de checkboxes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReapplyButtonLabel))]
    private bool _isSelectingTargets;

    public string ReapplyButtonLabel => IsSelectingTargets ? "CONFIRMAR REAPLICAÇÃO" : "REAPLICAR";

    /// <summary>True entre o primeiro clique em "Desinstalar" (arma a confirmação) e o segundo
    /// (confirma) — ver <see cref="InstalledThemesViewModel.UninstallAsync"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UninstallButtonLabel))]
    private bool _isConfirmingUninstall;

    public string UninstallButtonLabel => IsConfirmingUninstall ? "CONFIRMAR" : "DESINSTALAR";
}

/// <summary>Uma opção do seletor de paleta — <see cref="Id"/> nulo representa a paleta padrão
/// do tema (<c>configs/</c>, sem override).</summary>
public sealed record PaletteOption(string? Id, string Name);
