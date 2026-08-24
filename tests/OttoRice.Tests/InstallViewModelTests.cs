using NSubstitute;
using OttoRice.Common;
using OttoRice.Features.BackupRestore;
using OttoRice.Features.ThemeImport;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;

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
            history);
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
