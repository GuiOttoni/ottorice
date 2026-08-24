using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeInstall;

namespace OttoRice;

public sealed class MainViewModel(InstallViewModel install, BackupsViewModel backups)
{
    public InstallViewModel Install { get; } = install;
    public BackupsViewModel Backups { get; } = backups;
}
