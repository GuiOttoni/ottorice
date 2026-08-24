namespace OttoRice.Features.ThemeInstall;

/// <summary>Estado visual de um step do pipeline, pra visualização gráfica na UI.</summary>
public enum InstallStepState
{
    Pending,
    Running,
    Success,
    Failed,
    Compensated,
}
