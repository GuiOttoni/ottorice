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
                // Mesmo motivo do Control acima: sem isto a aba "Temas instalados" abre em
                // branco até o usuário clicar em Atualizar, mesmo com temas instalados (visto
                // no dogfooding — confirmado com Catppuccin Everywhere + Phosphor instalados).
                await vm.InstalledThemes.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Falha ao carregar o estado inicial da janela");
            }
        }
    }
}
