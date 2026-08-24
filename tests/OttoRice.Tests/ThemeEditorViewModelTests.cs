using System.Text.Json;
using NSubstitute;
using OttoRice.Features.ThemeEditor;
using OttoRice.Features.ThemeEditor.Models;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Tests;

/// <summary>
/// Editor de manifesto (seção 12.1 do plano de evolução): carrega um `rice-manifest.json`
/// existente, edita via ViewModel e salva de volta reaproveitando `ManifestValidator.Validate`
/// (nunca reimplementando regras). Os testes de round-trip rodam sobre os três temas de
/// exemplo reais (`examples/blackturq`, `examples/voidhaze`, `examples/catppuccin`) para
/// garantir que editar-e-salvar sem alterar nada não corrompe um manifesto válido.
/// </summary>
public class ThemeEditorViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-editor").FullName;

    private static string ExamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "examples")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "examples");
        }
    }

    private static ThemeEditorViewModel CreateVm() => new(Substitute.For<IThemeFilePicker>());

    private string CopyManifestToTempDir(string exampleName)
    {
        var sourceDir = Path.Combine(ExamplesDir, exampleName);
        var sourceManifest = Path.Combine(sourceDir, ThemeFetcher.ManifestFileName);
        var targetDir = Path.Combine(_dir, exampleName);
        Directory.CreateDirectory(targetDir);
        var targetManifest = Path.Combine(targetDir, ThemeFetcher.ManifestFileName);
        File.Copy(sourceManifest, targetManifest);
        return targetManifest;
    }

    [Fact]
    public async Task Load_populates_fields_from_manifest()
    {
        var manifestPath = CopyManifestToTempDir("blackturq");
        var vm = CreateVm();

        var loaded = await vm.LoadAsync(manifestPath);

        Assert.True(loaded);
        Assert.True(vm.IsLoaded);
        Assert.Equal("blackturq-minimal", vm.ThemeId);
        Assert.Equal("BlackTurq Minimal", vm.Name);
        Assert.Equal("ottorice", vm.Author);
        Assert.Equal(2, vm.Dependencies.Count);
        Assert.Equal(4, vm.Targets.Count);
        Assert.Empty(vm.ValidationErrors);

        var wtTarget = vm.Targets.Single(t => t.App == "windows_terminal");
        Assert.Equal("merge_scheme", wtTarget.Action);
        Assert.True(wtTarget.SetAsDefault);
        Assert.True(wtTarget.IsWindowsTerminal);
    }

    [Fact]
    public async Task Load_accepts_a_theme_directory_instead_of_the_manifest_file()
    {
        var manifestPath = CopyManifestToTempDir("voidhaze");
        var themeDir = Path.GetDirectoryName(manifestPath)!;
        var vm = CreateVm();

        var loaded = await vm.LoadAsync(themeDir);

        Assert.True(loaded);
        Assert.Equal("voidhaze", vm.ThemeId);
    }

    [Fact]
    public async Task Load_of_missing_file_fails_without_throwing()
    {
        var vm = CreateVm();
        var loaded = await vm.LoadAsync(Path.Combine(_dir, "does-not-exist.json"));

        Assert.False(loaded);
        Assert.False(vm.IsLoaded);
        Assert.Contains("não encontrado", vm.StatusMessage);
    }

    [Fact]
    public async Task Save_without_loading_is_disabled()
    {
        var vm = CreateVm();
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_rejects_invalid_theme_id_and_does_not_write_the_file()
    {
        var manifestPath = CopyManifestToTempDir("blackturq");
        var originalContent = await File.ReadAllTextAsync(manifestPath);
        var vm = CreateVm();
        await vm.LoadAsync(manifestPath);

        vm.ThemeId = "Não é Kebab Case";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.ValidationErrors);
        Assert.Contains(vm.ValidationErrors, e => e.Contains("themeId"));
        Assert.StartsWith("❌", vm.StatusMessage);
        Assert.Equal(originalContent, await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task Save_rejects_an_app_action_combination_outside_the_whitelist()
    {
        var manifestPath = CopyManifestToTempDir("blackturq");
        var vm = CreateVm();
        await vm.LoadAsync(manifestPath);

        // Força uma combinação inválida diretamente no modelo editável (contornando o
        // dropdown filtrado da UI) para provar que o Validate() do save ainda barra.
        var target = vm.Targets.First(t => t.App == "glazewm");
        target.Action = "merge_scheme"; // não permitido para glazewm

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Contains(vm.ValidationErrors, e => e.Contains("não permitida"));
    }

    [Fact]
    public void Adding_a_target_only_offers_whitelisted_apps()
    {
        var vm = CreateVm();
        Assert.NotEmpty(vm.AvailableApps);
        Assert.Contains("glazewm", vm.AvailableApps);
        Assert.DoesNotContain("arbitrary-app", vm.AvailableApps);
    }

    [Fact]
    public void Changing_the_app_of_a_target_narrows_allowed_actions_and_resets_an_invalid_action()
    {
        var target = new EditableTarget { App = "windows_terminal", Action = "merge_scheme" };

        target.App = "glazewm";

        Assert.Equal(["override"], target.AllowedActions);
        Assert.Equal("override", target.Action); // merge_scheme não é permitido para glazewm
    }

    [Theory]
    [InlineData("blackturq")]
    [InlineData("voidhaze")]
    [InlineData("catppuccin")]
    public async Task Round_trip_load_edit_nothing_save_reload_does_not_corrupt_a_valid_manifest(string exampleName)
    {
        var manifestPath = CopyManifestToTempDir(exampleName);

        var vm1 = CreateVm();
        Assert.True(await vm1.LoadAsync(manifestPath));
        Assert.Empty(vm1.ValidationErrors);
        await vm1.SaveCommand.ExecuteAsync(null);
        Assert.StartsWith("✅", vm1.StatusMessage);

        // Reabre o que acabou de ser salvo com um segundo editor, comparando contra o manifesto
        // original interpretado pelo mesmo parser que o resto do app usa para ler um tema —
        // não uma comparação textual, e sim de conteúdo (RiceManifest a RiceManifest).
        var original = ManifestValidator.Parse(
            await File.ReadAllTextAsync(Path.Combine(ExamplesDir, exampleName, ThemeFetcher.ManifestFileName)));
        var roundTripped = ManifestValidator.Parse(await File.ReadAllTextAsync(manifestPath));

        Assert.True(original.IsSuccess);
        Assert.True(roundTripped.IsSuccess, roundTripped.Error);

        AssertManifestsEquivalent(original.Value!, roundTripped.Value!);

        // O save também precisa continuar carregável por um segundo ThemeEditorViewModel,
        // não só pelo ManifestValidator direto (prova o caminho de uso real da UI).
        var vm2 = CreateVm();
        Assert.True(await vm2.LoadAsync(manifestPath));
        Assert.Empty(vm2.ValidationErrors);
        Assert.Equal(vm1.ThemeId, vm2.ThemeId);
        Assert.Equal(vm1.Targets.Count, vm2.Targets.Count);
    }

    private static void AssertManifestsEquivalent(RiceManifest expected, RiceManifest actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.ThemeId, actual.ThemeId);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Author, actual.Author);
        Assert.Equal(expected.Preview, actual.Preview);

        Assert.Equal(expected.Dependencies.Count, actual.Dependencies.Count);
        for (var i = 0; i < expected.Dependencies.Count; i++)
        {
            Assert.Equal(expected.Dependencies[i].WingetId, actual.Dependencies[i].WingetId);
            Assert.Equal(expected.Dependencies[i].MinVersion, actual.Dependencies[i].MinVersion);
        }

        Assert.Equal(expected.Targets.Count, actual.Targets.Count);
        for (var i = 0; i < expected.Targets.Count; i++)
        {
            Assert.Equal(expected.Targets[i].App, actual.Targets[i].App);
            Assert.Equal(expected.Targets[i].Action, actual.Targets[i].Action);
            Assert.Equal(expected.Targets[i].Source, actual.Targets[i].Source);
            Assert.Equal(expected.Targets[i].SetAsDefault, actual.Targets[i].SetAsDefault);
            Assert.Equal(expected.Targets[i].Settings, actual.Targets[i].Settings);
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
