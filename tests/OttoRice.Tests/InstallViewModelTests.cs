using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;
using OttoRice.Features.ThemeToggle;

namespace OttoRice.Tests;

public class InstallViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ottorice-vm").FullName;

    private static FetchedTheme Theme(string dir) => new(dir, new RiceManifest
    {
        SchemaVersion = "1.0",
        ThemeId = "tema-vm",
        Name = "Tema VM",
        Author = "teste",
        Targets = [new RiceTarget { App = "glazewm", Action = "override", Source = "c.yaml" }],
        Dependencies = [new RiceDependency { WingetId = "glazewm.glazewm" }],
    });

    private sealed class NoopStep(bool succeeds = true) : IInstallStep
    {
        public string Name => "noop";
        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default) =>
            Task.FromResult(succeeds ? Result.Ok() : Result.Fail("passo falhou"));
    }

    private InstallViewModel CreateVm(IThemeFetcher fetcher, bool pipelineSucceeds, out InstallHistoryStore history)
    {
        history = new InstallHistoryStore(_dir);
        return new InstallViewModel(
            fetcher,
            new InstallPipeline([new NoopStep(pipelineSucceeds)]),
            history,
            new ThemeStateStore(_dir),
            Substitute.For<IThemeFilePicker>());
    }

    [Fact]
    public async Task Fetch_populates_preview_data_and_enables_install()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        var vm = CreateVm(fetcher, pipelineSucceeds: true, out _);
        vm.ThemeUrl = "https://github.com/owner/repo";

        Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.FetchCommand.ExecuteAsync(null);

        Assert.True(vm.HasPreview);
        Assert.Contains("Tema VM", vm.ThemeTitle);
        Assert.Equal(["GlazeWM v3"], vm.AffectedApps);
        Assert.Equal(["glazewm.glazewm"], vm.Dependencies);
        Assert.True(vm.InstallCommand.CanExecute(null));

        // Visualização gráfica das etapas já aparece no preview, antes de instalar.
        var step = Assert.Single(vm.Steps);
        Assert.Equal("noop", step.Name);
        Assert.Equal(InstallStepState.Pending, step.State);
    }

    [Fact]
    public async Task Install_transitions_step_state_to_success()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        var vm = CreateVm(fetcher, pipelineSucceeds: true, out _);
        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        var step = Assert.Single(vm.Steps);
        Assert.Equal(InstallStepState.Success, step.State);
    }

    [Fact]
    public async Task Failed_install_marks_the_failing_step_as_failed()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        var vm = CreateVm(fetcher, pipelineSucceeds: false, out _);
        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        var step = Assert.Single(vm.Steps);
        Assert.Equal(InstallStepState.Failed, step.State);
    }

    [Fact]
    public async Task Browse_sets_url_from_picker_and_fetches()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));
        var picker = Substitute.For<IThemeFilePicker>();
        picker.PickManifestAsync().Returns(@"C:\temas\meu\rice-manifest.json");

        var vm = new InstallViewModel(
            fetcher,
            new InstallPipeline([new NoopStep()]),
            new InstallHistoryStore(_dir),
            new ThemeStateStore(_dir),
            picker);

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\temas\meu\rice-manifest.json", vm.ThemeUrl);
        await fetcher.Received(1).FetchAsync(@"C:\temas\meu\rice-manifest.json", Arg.Any<CancellationToken>());
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public async Task Browse_cancelled_by_user_changes_nothing()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        var picker = Substitute.For<IThemeFilePicker>();
        picker.PickManifestAsync().Returns((string?)null);

        var vm = new InstallViewModel(
            fetcher,
            new InstallPipeline([new NoopStep()]),
            new InstallHistoryStore(_dir),
            new ThemeStateStore(_dir),
            picker);
        vm.ThemeUrl = "valor-anterior";

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal("valor-anterior", vm.ThemeUrl);
        await fetcher.DidNotReceiveWithAnyArgs().FetchAsync(default!, default);
    }

    [Fact]
    public async Task Fetch_failure_shows_error_and_keeps_install_disabled()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Fail("repo não encontrado"));

        var vm = CreateVm(fetcher, pipelineSucceeds: true, out _);
        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);

        Assert.Contains("repo não encontrado", vm.StatusMessage);
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Successful_install_appends_history_record()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        var vm = CreateVm(fetcher, pipelineSucceeds: true, out var history);
        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("✅", vm.StatusMessage);
        var record = Assert.Single(await history.ReadAllAsync());
        Assert.Equal("tema-vm", record.ThemeId);
    }

    [Fact]
    public async Task Successful_install_records_state_for_the_toggle()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        // Step que popula o contexto como o pipeline real faria.
        var stateStore = new ThemeStateStore(_dir);
        var vm = new InstallViewModel(
            fetcher,
            new InstallPipeline([new PopulatingStep()]),
            new InstallHistoryStore(_dir),
            stateStore,
            Substitute.For<IThemeFilePicker>());

        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        var state = await stateStore.ReadAsync();
        Assert.Equal("tema-vm", state.ActiveThemeId);
        Assert.True(state.IsEnabled);
        Assert.Contains("glazewm", state.ManagedApps);
        Assert.Equal(@"C:\cfg\glazewm.yaml", state.GlazeWmConfigPath);
    }

    private sealed class PopulatingStep : IInstallStep
    {
        public string Name => "populating";

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            context.Operations.Add(new FileOperation(
                new RiceTarget { App = "glazewm", Action = "override", Source = "c.yaml" },
                "src.yaml", @"C:\cfg\glazewm.yaml"));
            return Task.FromResult(Result.Ok());
        }
    }

    [Fact]
    public async Task Failed_install_reports_rollback_and_records_nothing()
    {
        var fetcher = Substitute.For<IThemeFetcher>();
        fetcher.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Result<FetchedTheme>.Ok(Theme(_dir)));

        var vm = CreateVm(fetcher, pipelineSucceeds: false, out var history);
        vm.ThemeUrl = "https://github.com/owner/repo";
        await vm.FetchCommand.ExecuteAsync(null);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Contains("desfeitas", vm.StatusMessage);
        Assert.Empty(await history.ReadAllAsync());
        Assert.False(vm.IsBusy);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
