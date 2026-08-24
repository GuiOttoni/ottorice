using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OttoRice.AppRegistry;

namespace OttoRice.Features.ThemeEditor.Models;

/// <summary>
/// Linha editável de `targets[]`. `App` é restrito a `SupportedApps.All.Keys` e `Action` ao
/// `AllowedActions` do app selecionado — a UI nunca permite texto livre nesses dois campos,
/// é a mesma whitelist de segurança usada pelo resto do app (AppRegistry/SupportedApps.cs).
/// </summary>
public sealed partial class EditableTarget : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsTerminal))]
    [NotifyPropertyChangedFor(nameof(IsConfigureMod))]
    private string? _app;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowsTerminal))]
    [NotifyPropertyChangedFor(nameof(IsConfigureMod))]
    private string? _action;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private bool _setAsDefault;

    public ObservableCollection<EditableSetting> Settings { get; } = [];

    /// <summary>Ações permitidas para o app atualmente selecionado — vazio se nenhum app válido.</summary>
    public IReadOnlyList<string> AllowedActions =>
        App is not null && SupportedApps.All.TryGetValue(App, out var definition)
            ? [.. definition.AllowedActions.OrderBy(a => a)]
            : [];

    /// <summary>`setAsDefault` só faz sentido para `windows_terminal` (ação `merge_scheme`).</summary>
    public bool IsWindowsTerminal => App == "windows_terminal";

    /// <summary>Campos `settings[]` só se aplicam à ação `configure_mod` (mods Windhawk).</summary>
    public bool IsConfigureMod => Action == "configure_mod";

    partial void OnAppChanged(string? value)
    {
        OnPropertyChanged(nameof(AllowedActions));
        // Troca de app pode invalidar a ação escolhida (ex.: "override" não existe pra
        // windows_terminal) — nunca deixa uma combinação app/ação fora da whitelist.
        if (Action is not null && !AllowedActions.Contains(Action))
            Action = AllowedActions.Count > 0 ? AllowedActions[0] : null;
    }
}
