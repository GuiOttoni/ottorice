using CommunityToolkit.Mvvm.ComponentModel;

namespace OttoRice.Features.ThemeEditor.Models;

/// <summary>Par chave/valor editável de `RiceTarget.Settings` (só usado por `configure_mod`).</summary>
public sealed partial class EditableSetting : ObservableObject
{
    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;
}
