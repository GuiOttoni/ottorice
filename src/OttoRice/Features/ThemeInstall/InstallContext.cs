using System;
using System.Collections.Generic;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeInstall;

/// <summary>Uma operação concreta de arquivo derivada de um target do manifesto (TargetPath vazio = wallpaper).</summary>
public sealed record FileOperation(RiceTarget Target, string SourcePath, string TargetPath);

/// <summary>Estado compartilhado entre os steps do pipeline de instalação.</summary>
public sealed class InstallContext
{
    public required RiceManifest Manifest { get; init; }

    /// <summary>Diretório local com os arquivos do tema já baixados.</summary>
    public required string ThemeDirectory { get; init; }

    public Action<string>? Progress { get; init; }

    /// <summary>Notifica transições de estado de um step, pra visualização gráfica na UI.</summary>
    public Action<string, InstallStepState>? StepStateChanged { get; init; }

    public List<FileOperation> Operations { get; } = [];
    public BackupSessionInfo? BackupSession { get; set; }
    public string? PreviousWallpaperPath { get; set; }
    public List<string> WingetIdsInstalled { get; } = [];

    public void Report(string message) => Progress?.Invoke(message);
}
