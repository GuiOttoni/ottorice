using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace OttoRice.Features.ThemeImport;

/// <summary>Seleção de um manifesto de tema no disco (abstraída para o ViewModel ser testável).</summary>
public interface IThemeFilePicker
{
    Task<string?> PickManifestAsync();
}

public sealed class AvaloniaThemeFilePicker(Func<TopLevel?> topLevelAccessor) : IThemeFilePicker
{
    public async Task<string?> PickManifestAsync()
    {
        var topLevel = topLevelAccessor();
        if (topLevel is null)
            return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o manifesto do tema (rice-manifest.json)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Manifesto de tema") { Patterns = ["*.json"] },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Janela principal em tempo de chamada — o picker é resolvido antes dela existir.</summary>
    public static TopLevel? CurrentMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
