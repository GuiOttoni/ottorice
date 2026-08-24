using CommunityToolkit.Mvvm.ComponentModel;

namespace OttoRice.Features.ThemeEditor.Models;

/// <summary>Linha editável de `dependencies[]` — wingetId/minVersion, sem regra própria de validação
/// (a validação real continua sendo <see cref="OttoRice.Features.ThemeImport.ManifestValidator"/>).</summary>
public sealed partial class EditableDependency : ObservableObject
{
    [ObservableProperty]
    private string _wingetId = string.Empty;

    [ObservableProperty]
    private string _minVersion = string.Empty;
}
