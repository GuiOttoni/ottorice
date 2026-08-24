using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OttoRice;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Sem isto a aba "Tema ativo" abre dizendo que não há tema aplicado até o
        // usuário clicar em Atualizar, mesmo com um tema instalado (visto no dogfooding).
        if (DataContext is MainViewModel vm)
        {
            try
            {
                await vm.Control.RefreshCommand.ExecuteAsync(null);
                await vm.Backups.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Falha ao carregar o estado inicial da janela");
            }
        }
    }
}
