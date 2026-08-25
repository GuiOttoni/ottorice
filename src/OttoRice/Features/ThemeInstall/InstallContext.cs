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

    /// <summary>
    /// Índices (na lista <see cref="RiceManifest.Targets"/> do manifesto, na ordem em que
    /// aparecem) dos targets que o usuário escolheu aplicar nesta execução — toggle por
    /// componente (RF: ligar/desligar cada componente do tema na instalação/reaplicação).
    /// <c>null</c> = todos os targets do manifesto (comportamento padrão, retrocompatível).
    /// A validação do manifesto completo (<see cref="OttoRice.Features.ThemeImport.ManifestValidator"/>)
    /// roda antes disso e continua vendo o manifesto inteiro — este filtro só decide o que o
    /// <see cref="Steps.PlanStep"/> manda pro <see cref="TargetPlanner"/>, então targets
    /// desmarcados nunca chegam a ser planejados nem aplicados.
    /// </summary>
    public IReadOnlySet<int>? SelectedTargetIndexes { get; init; }

    public List<FileOperation> Operations { get; } = [];
    public BackupSessionInfo? BackupSession { get; set; }
    public string? PreviousWallpaperPath { get; set; }
    public List<string> WingetIdsInstalled { get; } = [];

    public void Report(string message) => Progress?.Invoke(message);
}
