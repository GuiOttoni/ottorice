using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;
using OttoRice.Features.ThemeToggle;
using OttoRice.Features.ThemeUninstall;

namespace OttoRice.Tests;

/// <summary>
/// Toggle por componente na aba "Temas instalados": o botão REAPLICAR é em dois cliques —
/// o primeiro busca os targets do tema (sem aplicar nada) pra deixar o usuário ligar/desligar
/// cada um; o segundo confirma com a seleção feita. Ver <see cref="InstalledThemesViewModel.ReapplyAsync"/>.
/// </summary>
public class InstalledThemesViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-installed-vm").FullName;
    private readonly IThemeFetcher _fetcher = Substitute.For<IThemeFetcher>();
    private readonly ThemeStateStore _stateStore;
    private InstallContext? _capturedContext;

    public InstalledThemesViewModelTests() => _stateStore = new ThemeStateStore(_dir);

    private sealed class CapturingStep(Action<InstallContext> capture) : IInstallStep
    {
        public string Name => "captura";

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            capture(context);
            return Task.FromResult(Result.Ok());
        }
    }

    private InstalledThemesViewModel CreateVm()
    {
        var toggle = new ThemeToggleService(
            Substitute.For<IProcessRunner>(),
            Substitute.For<IWallpaperService>(),
            _stateStore,
            Substitute.For<IExecutableResolver>());
        var reapplyPipeline = new InstallPipeline([new CapturingStep(ctx => _capturedContext = ctx)]);
        var reapply = new ReapplyThemeService(_fetcher, reapplyPipeline, _stateStore);
        var uninstall = new UninstallService(
            new InstallHistoryStore(_dir),
            new BackupSessionStore(Path.Combine(_dir, "backups")),
            _stateStore,
            toggle,
            Substitute.For<IWinGetClient>());

        return new InstalledThemesViewModel(toggle, reapply, uninstall);
    }

    private static RiceManifest TwoTargetManifest() => new()
    {
        ThemeId = "t",
        Name = "T",
        Targets =
        [
            new RiceTarget { App = "glazewm", Action = "override", Source = "s1" },
            new RiceTarget { App = "wallpaper", Action = "set", Source = "s2" },
        ],
    };

    private async Task SeedInstalledThemeAsync()
    {
        await _stateStore.UpsertThemeAsync(new ThemeState
        {
            ThemeId = "t", ThemeName = "Tema T", IsEnabled = true, SourceUrl = "https://github.com/o/r",
        }, makeActive: true);
        _fetcher.FetchAsync("https://github.com/o/r", Arg.Any<CancellationToken>())
                .Returns(Result<FetchedTheme>.Ok(new FetchedTheme("dir", TwoTargetManifest())));
    }

    [Fact]
    public async Task First_click_on_reapply_only_populates_targets_without_reapplying_anything()
    {
        await SeedInstalledThemeAsync();
        var vm = CreateVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        var item = Assert.Single(vm.Themes);

        await vm.ReapplyCommand.ExecuteAsync(item);

        Assert.True(item.IsSelectingTargets);
        Assert.Equal(2, item.Targets.Count);
        Assert.All(item.Targets, t => Assert.True(t.IsSelected)); // marcados por padrão
        Assert.Null(_capturedContext); // pipeline real não rodou ainda
        Assert.Empty(vm.Log);
    }

    [Fact]
    public async Task Second_click_reapplies_only_the_targets_still_selected()
    {
        await SeedInstalledThemeAsync();
        var vm = CreateVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        var item = Assert.Single(vm.Themes);

        await vm.ReapplyCommand.ExecuteAsync(item); // 1º clique: popula os targets
        item.Targets[1].IsSelected = false; // desmarca o wallpaper

        await vm.ReapplyCommand.ExecuteAsync(item); // 2º clique: confirma

        Assert.NotNull(_capturedContext);
        Assert.Equal(new HashSet<int> { 0 }, _capturedContext!.SelectedTargetIndexes);
        Assert.StartsWith("✅", vm.StatusMessage);
        // A seleção fecha depois de confirmar — refresh recarrega a lista do zero.
        var refreshed = Assert.Single(vm.Themes);
        Assert.False(refreshed.IsSelectingTargets);
    }

    [Fact]
    public async Task Cancel_reapply_selection_discards_the_pending_choice_without_reapplying()
    {
        await SeedInstalledThemeAsync();
        var vm = CreateVm();
        await vm.RefreshCommand.ExecuteAsync(null);
        var item = Assert.Single(vm.Themes);
        await vm.ReapplyCommand.ExecuteAsync(item);

        vm.CancelReapplySelectionCommand.Execute(item);

        Assert.False(item.IsSelectingTargets);
        Assert.Empty(item.Targets);
        Assert.Null(_capturedContext);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
