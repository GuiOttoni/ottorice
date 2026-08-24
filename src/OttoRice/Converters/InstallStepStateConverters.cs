using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using OttoRice.Features.ThemeInstall;

namespace OttoRice.Converters;

/// <summary>Glyph/brush por <see cref="InstallStepState"/> pra visualização gráfica do pipeline.</summary>
public sealed class InstallStepStateToGlyphConverter : IValueConverter
{
    public static readonly InstallStepStateToGlyphConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InstallStepState state
            ? state switch
            {
                InstallStepState.Pending => "○",
                InstallStepState.Running => "◐",
                InstallStepState.Success => "✓",
                InstallStepState.Failed => "✗",
                InstallStepState.Compensated => "↺",
                _ => "○",
            }
            : "○";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InstallStepStateToBrushConverter : IValueConverter
{
    public static readonly InstallStepStateToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is InstallStepState state
            ? state switch
            {
                InstallStepState.Pending => "TextTertiaryBrush",
                InstallStepState.Running => "AccentBrush",
                InstallStepState.Success => "SuccessBrush",
                InstallStepState.Failed => "AccentBrush",
                InstallStepState.Compensated => "GoldBrush",
                _ => "TextTertiaryBrush",
            }
            : "TextTertiaryBrush";

        // Mesmas chaves do OttoTheme.axaml — resolvidas em runtime porque DynamicResource
        // não pode ser apontado por uma chave vinda de binding.
        return Application.Current?.TryGetResource(key, null, out var brush) == true && brush is IBrush ib
            ? ib
            : Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
