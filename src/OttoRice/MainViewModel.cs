using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeToggle;

namespace OttoRice;

public sealed class MainViewModel(
    InstallViewModel install,
    ThemeControlViewModel control,
    BackupsViewModel backups)
{
    public InstallViewModel Install { get; } = install;
    public ThemeControlViewModel Control { get; } = control;
    public BackupsViewModel Backups { get; } = backups;
}
