using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace OttoRice.Features.BackupRestore;

/// <summary>Histórico de sessões de backup com restauração manual (RF-08/RF-09).</summary>
public partial class BackupsViewModel(BackupSessionStore store, ILogger<BackupsViewModel>? logger = null) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private BackupSessionInfo? _selectedSession;

    [ObservableProperty]
    private string _statusMessage = "";

    public ObservableCollection<BackupSessionInfo> Sessions { get; } = [];

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Sessions.Clear();
            foreach (var session in await store.ListSessionsAsync())
                Sessions.Add(session);
            StatusMessage = Sessions.Count == 0 ? "Nenhum backup ainda." : "";
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Falha ao listar sessões de backup.");
            StatusMessage = $"❌ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRestore() => !IsBusy && SelectedSession is not null;

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        var session = SelectedSession!;
        IsBusy = true;
        StatusMessage = $"Restaurando backup de '{session.ThemeId}'...";
        try
        {
            var result = await store.RestoreAsync(session.Id);
            if (!result.IsSuccess)
                logger?.LogError("Falha ao restaurar sessão de backup '{SessionId}': {Error}", session.Id, result.Error);
            StatusMessage = result.IsSuccess
                ? $"✅ Configurações de antes do tema '{session.ThemeId}' restauradas."
                : $"❌ {result.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
