using OttoRice.Features.ThemeInstall;

namespace OttoRice.Tests;

public class WindhawkSettingsFlattenerTests
{
    [Fact]
    public void Flattens_the_real_windhawk_ui_textual_format()
    {
        // Exemplo real (modo textual da própria UI do Windhawk / windows-11-start-menu-styler).
        const string yaml = """
            theme: Down Aero
            disableNewStartMenuLayout: ''
            styleConstants:
              - ''
            controlStyles:
              - target: ''
                styles:
                  - ''
            themeResourceVariables:
              - ''
            webContentStyles:
              - target: ''
                styles:
                  - ''
            webContentCustomJs: ''
            """;

        var flat = WindhawkSettingsFlattener.Flatten(yaml);

        Assert.Equal("Down Aero", flat["theme"]);
        Assert.Equal("", flat["disableNewStartMenuLayout"]);
        Assert.Equal("", flat["styleConstants[0]"]);
        Assert.Equal("", flat["controlStyles[0].target"]);
        Assert.Equal("", flat["controlStyles[0].styles[0]"]);
        Assert.Equal("", flat["themeResourceVariables[0]"]);
        Assert.Equal("", flat["webContentStyles[0].target"]);
        Assert.Equal("", flat["webContentStyles[0].styles[0]"]);
        Assert.Equal("", flat["webContentCustomJs"]);
    }

    [Fact]
    public void Flattens_multiple_array_items_and_multiline_scalars()
    {
        const string yaml = """
            controlStyles:
              - target: '.taskbar-icon'
                styles:
                  - 'color: red;'
                  - 'background: blue;'
              - target: '.clock'
                styles:
                  - |
                    font-weight: bold;
                    font-size: 14px;
            """;

        var flat = WindhawkSettingsFlattener.Flatten(yaml);

        Assert.Equal(".taskbar-icon", flat["controlStyles[0].target"]);
        Assert.Equal("color: red;", flat["controlStyles[0].styles[0]"]);
        Assert.Equal("background: blue;", flat["controlStyles[0].styles[1]"]);
        Assert.Equal(".clock", flat["controlStyles[1].target"]);
        Assert.Contains("font-weight: bold;", flat["controlStyles[1].styles[0]"]);
    }

    [Fact]
    public void Empty_yaml_produces_no_pairs()
    {
        Assert.Empty(WindhawkSettingsFlattener.Flatten(""));
    }
}
