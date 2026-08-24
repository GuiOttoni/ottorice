using CommunityToolkit.Mvvm.ComponentModel;

namespace OttoRice.Features.ThemeInstall;

/// <summary>Um step do pipeline representado na visualização gráfica da instalação.</summary>
public sealed partial class StepStatusItem(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private InstallStepState _state = InstallStepState.Pending;
}
