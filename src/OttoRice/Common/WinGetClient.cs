using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OttoRice.Common;

public interface IWinGetClient
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<bool> IsInstalledAsync(string packageId, CancellationToken ct = default);
    Task<Result> InstallAsync(string packageId, CancellationToken ct = default);
    Task<Result> UninstallAsync(string packageId, CancellationToken ct = default);
}

/// <summary>
/// Wrapper do WinGet CLI. Instalações devem ser chamadas em série — o WinGet
/// não suporta operações concorrentes (lock no banco local).
/// </summary>
public sealed partial class WinGetClient(IProcessRunner runner) : IWinGetClient
{
    // APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED (0x8A15002B)
    private const int AlreadyInstalled = unchecked((int)0x8A15002B);

    // APPINSTALLER_CLI_ERROR_NO_APPLICATIONS_FOUND (0x8A150014)
    private const int NoPackageFound = unchecked((int)0x8A150014);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\.\-\+_]*$")]
    private static partial Regex PackageIdPattern();

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await runner.RunAsync("winget", "--version", ct);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsInstalledAsync(string packageId, CancellationToken ct = default)
    {
        ValidatePackageId(packageId);
        var result = await runner.RunAsync(
            "winget", $"list --id {packageId} --exact --accept-source-agreements --disable-interactivity", ct);
        return result.ExitCode == 0;
    }

    public async Task<Result> InstallAsync(string packageId, CancellationToken ct = default)
    {
        ValidatePackageId(packageId);
        var result = await runner.RunAsync(
            "winget",
            $"install --id {packageId} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            ct);

        if (result.ExitCode == 0 || result.ExitCode == AlreadyInstalled)
            return Result.Ok();

        return Result.Fail(
            $"WinGet falhou ao instalar '{packageId}' (exit 0x{result.ExitCode:X8}). {Truncate(result.StandardError, 300)}");
    }

    public async Task<Result> UninstallAsync(string packageId, CancellationToken ct = default)
    {
        ValidatePackageId(packageId);
        var result = await runner.RunAsync(
            "winget",
            $"uninstall --id {packageId} --exact --silent --accept-source-agreements --disable-interactivity",
            ct);

        // "Nenhum pacote encontrado" = já desinstalado; desinstalação é idempotente.
        if (result.ExitCode == 0 || result.ExitCode == NoPackageFound)
            return Result.Ok();

        return Result.Fail(
            $"WinGet falhou ao desinstalar '{packageId}' (exit 0x{result.ExitCode:X8}). {Truncate(result.StandardError, 300)}");
    }

    // O id vem do manifesto (terceiros): validar o formato impede injeção de argumentos no CLI.
    private static void ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !PackageIdPattern().IsMatch(packageId))
            throw new ArgumentException($"Id de pacote WinGet inválido: '{packageId}'.", nameof(packageId));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
