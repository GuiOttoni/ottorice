using CommunityToolkit.Mvvm.ComponentModel;
using OttoRice.AppRegistry;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Uma linha "aplicar este componente?" na prévia de instalação/reaplicação — o usuário pode
/// desligar targets individualmente antes de instalar/reaplicar (pedido: "eu gostaria de poder
/// desligar e ligar cada um dos componentes separadamente"). Todos vêm marcados por padrão.
///
/// Guarda o <see cref="Index"/> do target na lista original do manifesto (não uma referência/
/// igualdade de <see cref="RiceTarget"/>, que é um record — dois targets podem ser
/// estruturalmente iguais no mesmo manifesto) — é esse índice que volta pro
/// <see cref="InstallContext.SelectedTargetIndexes"/> pra filtrar o que o <see cref="TargetPlanner"/> planeja.
/// </summary>
public sealed partial class TargetSelectionItem : ObservableObject
{
    public TargetSelectionItem(int index, RiceTarget target)
    {
        Index = index;
        Target = target;
        Label = BuildLabel(target);
    }

    public int Index { get; }
    public RiceTarget Target { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    private static string BuildLabel(RiceTarget target)
    {
        var appName = target.App is not null && SupportedApps.All.TryGetValue(target.App, out var app)
            ? app.DisplayName
            : target.App ?? "?";
        var actionLabel = target.Action switch
        {
            "override" => "sobrescrever config",
            "merge_scheme" => "injetar esquema de cores",
            "set" => "definir papel de parede",
            "configure_mod" => "configurar mod Windhawk",
            _ => target.Action ?? "",
        };
        return $"{appName} — {actionLabel}";
    }
}
