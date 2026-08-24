using NSubstitute;
using OttoRice.Common;

namespace OttoRice.Tests;

public class WinGetClientTests
{
    private const int AlreadyInstalled = unchecked((int)0x8A15002B);

    private static (WinGetClient Client, IProcessRunner Runner) Create(int exitCode, string stderr = "")
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(new ProcessResult(exitCode, "", stderr));
        return (new WinGetClient(runner), runner);
    }

    [Fact]
    public async Task Install_success_on_exit_zero()
    {
        var (client, runner) = Create(0);
        var result = await client.InstallAsync("glazewm.glazewm");

        Assert.True(result.IsSuccess);
        await runner.Received(1).RunAsync("winget",
            Arg.Is<string>(a => a.Contains("--id glazewm.glazewm") && a.Contains("--silent")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_treats_already_installed_as_success()
    {
        var (client, _) = Create(AlreadyInstalled);
        Assert.True((await client.InstallAsync("AmN.yasb")).IsSuccess);
    }

    [Fact]
    public async Task Install_failure_returns_error_with_exit_code()
    {
        var (client, _) = Create(-1978335212, "não encontrado");
        var result = await client.InstallAsync("Pacote.Inexistente");

        Assert.False(result.IsSuccess);
        Assert.Contains("Pacote.Inexistente", result.Error);
    }

    [Theory]
    [InlineData("id com espaço --force")]
    [InlineData("id;cmd")]
    [InlineData("\"quoted\"")]
    [InlineData("")]
    public async Task Malicious_package_id_is_rejected_before_reaching_cli(string badId)
    {
        var (client, runner) = Create(0);
        await Assert.ThrowsAsync<ArgumentException>(() => client.InstallAsync(badId));
        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default);
    }

    [Fact]
    public async Task IsInstalled_maps_exit_code()
    {
        var (installed, _) = Create(0);
        Assert.True(await installed.IsInstalledAsync("glazewm.glazewm"));

        var (missing, _) = Create(-1978335212);
        Assert.False(await missing.IsInstalledAsync("glazewm.glazewm"));
    }
}
