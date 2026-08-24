using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeToggle;

namespace OttoRice;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusMessage = "Pronto.";

    public MainViewModel(InstallViewModel install, ThemeControlViewModel control, BackupsViewModel backups)
    {
        Install = install;
        Control = control;
        Backups = backups;

        // A barra de status do rodapé espelha a última mensagem de qualquer aba,
        // como no OttoInfra — o usuário não perde o retorno ao trocar de aba.
        Observe(install, () => install.StatusMessage);
        Observe(control, () => control.StatusMessage);
        Observe(backups, () => backups.StatusMessage);
    }

    public InstallViewModel Install { get; }
    public ThemeControlViewModel Control { get; }
    public BackupsViewModel Backups { get; }

    private void Observe(INotifyPropertyChanged source, System.Func<string> read)
    {
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstallViewModel.StatusMessage))
                StatusMessage = read();
        };
    }
}
