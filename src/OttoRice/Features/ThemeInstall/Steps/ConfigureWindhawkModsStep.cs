using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Instala/configura mods do Windhawk (ação "configure_mod") via `windhawk-cli`. Melhor
/// esforço, nunca derruba o pipeline: Windhawk é pré-requisito manual (não é instalado pelo
/// OttoRice — só a build 2.0 alpha tem o CLI, ainda fora do WinGet), e escrita no
/// windhawk-cli exige elevação (UAC).
///
/// Executa via PowerShell com `-EncodedCommand` (script inteiro em base64 UTF-16LE) em vez
/// de um .cmd — cada valor de settings vira um literal de string do PowerShell (aspas
/// simples, só `'` precisa ser escapado), então CSS/JS de verdade com `&amp;`, `|`, `"`, `%`
/// ou quebras de linha passa sem precisar banir caractere nenhum (ao contrário de um .cmd,
/// onde esses são metacaracteres do próprio shell). Todas as chamadas de um mesmo tema são
/// batidas num único script elevado — um prompt de UAC só, não um por mod.
/// </summary>
public sealed class ConfigureWindhawkModsStep(
    IExecutableResolver resolver,
    IProcessRunner runner,
    ILogger<ConfigureWindhawkModsStep>? logger = null) : IInstallStep
{
    public string Name => "Mods do Windhawk";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        var modOps = context.Operations.Where(op => op.Target.Action == "configure_mod").ToList();
        if (modOps.Count == 0)
            return Result.Ok();

        var cliPath = resolver.Resolve("windhawk-cli");
        if (cliPath is null)
        {
            logger?.LogWarning("windhawk-cli não encontrado — pulando {Count} mod(s) do Windhawk.", modOps.Count);
            context.Report(
                "⚠ Windhawk não encontrado — pulando os mods do tema. " +
                "Instale manualmente em https://windhawk.net/ (build 2.0 ou mais recente) e reaplique o tema.");
            return Result.Ok();
        }

        var mods = new List<(string Id, Dictionary<string, string> Settings)>();
        foreach (var op in modOps)
        {
            var settings = new Dictionary<string, string>();
            if (op.SourcePath.Length > 0)
            {
                try
                {
                    var yaml = await File.ReadAllTextAsync(op.SourcePath, ct);
                    foreach (var (key, value) in WindhawkSettingsFlattener.Flatten(yaml))
                        settings[key] = value;
                }
                catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
                {
                    logger?.LogWarning(ex, "Falha ao ler/parsear settings de '{App}' em '{Path}'.", op.Target.App, op.SourcePath);
                    context.Report($"⚠ Não foi possível ler o YAML de settings de '{op.Target.App}' — ignorado.");
                }
            }
            // Settings inline no manifesto têm prioridade sobre o YAML (permite sobrescrever
            // um valor específico sem reescrever o arquivo inteiro).
            foreach (var (key, value) in op.Target.Settings ?? [])
                settings[key] = value;

            mods.Add((op.Target.App!, settings));
        }

        var installedIds = await GetInstalledModIdsAsync(cliPath, ct);

        var logPath = Path.GetTempFileName();
        try
        {
            var script = BuildPowerShellScript(cliPath, logPath, mods, installedIds);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            context.Report($"Configurando {mods.Count} mod(s) do Windhawk (pode pedir confirmação do UAC)...");
            var exitCode = await runner.RunElevatedAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                ct);

            var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath, ct) : "";
            if (exitCode is null)
            {
                logger?.LogWarning("Configuração dos mods do Windhawk cancelada (UAC negado pelo usuário).");
                context.Report("⚠ Confirmação do UAC negada — mods do Windhawk não configurados.");
            }
            else if (exitCode != 0)
            {
                logger?.LogWarning(
                    "windhawk-cli saiu com código {ExitCode} configurando mods. Log: {Log}", exitCode, log);
                context.Report($"⚠ Falha ao configurar mods do Windhawk (código {exitCode}) — tema aplicado mesmo assim.");
            }
            else
            {
                logger?.LogInformation("Mods do Windhawk configurados: {Mods}.",
                    string.Join(", ", mods.Select(m => m.Id)));
                context.Report("Mods do Windhawk configurados.");
                EnsureWindhawkRunning(context);
            }
        }
        finally
        {
            TryDelete(logPath);
        }

        // Melhor esforço: nunca falha o pipeline (Windhawk é opcional/manual).
        return Result.Ok();
    }

    /// <summary>
    /// Lê os mods já instalados sem elevação (leitura funciona sem admin) — evita
    /// `mod install` desnecessário num mod já presente, que reseta os settings dele pro
    /// default (confirmado em teste real: reinstalar apaga customizações anteriores).
    /// </summary>
    private async Task<IReadOnlySet<string>> GetInstalledModIdsAsync(string cliPath, CancellationToken ct)
    {
        try
        {
            var result = await runner.RunAsync(cliPath, "mod list --json", ct);
            if (result.ExitCode != 0)
                return new HashSet<string>();

            using var doc = JsonDocument.Parse(result.StandardOutput);
            var ids = doc.RootElement.GetProperty("data").GetProperty("mods")
                .EnumerateArray()
                .Select(m => m.GetProperty("id").GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            return ids;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "Falha ao ler mods instalados do Windhawk — tratando todos como não instalados.");
            return new HashSet<string>();
        }
    }

    private static string BuildPowerShellScript(
        string cliPath,
        string logPath,
        IReadOnlyList<(string Id, Dictionary<string, string> Settings)> mods,
        IReadOnlySet<string> installedIds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"$cli = {PsQuote(cliPath)}");
        sb.AppendLine($"$log = {PsQuote(logPath)}");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");

        foreach (var (id, settings) in mods)
        {
            if (!installedIds.Contains(id))
            {
                sb.AppendLine($"& $cli mod install {PsQuote(id)} *>> $log");
                sb.AppendLine("if ($LASTEXITCODE -ne 0) { exit 1 }");
            }

            if (settings.Count > 0)
            {
                var pairs = string.Join(" ", settings.Select(kv => PsQuote($"{kv.Key}={kv.Value}")));
                sb.AppendLine($"& $cli mod settings set {PsQuote(id)} {pairs} *>> $log");
                sb.AppendLine("if ($LASTEXITCODE -ne 0) { exit 1 }");
            }
        }
        sb.AppendLine("exit 0");
        return sb.ToString();
    }

    /// <summary>Literal de string do PowerShell com aspas simples — só `'` precisa ser escapado (dobrado).</summary>
    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>
    /// O componente de sessão do Windhawk (windhawk.exe, sem elevação — o serviço de
    /// verdade roda em outra sessão) precisa estar rodando pra injetar os mods recém
    /// habilitados nos processos já abertos (explorer.exe etc.). Se já estiver, é
    /// idempotente — o próprio Windhawk detecta e não abre uma segunda instância.
    /// </summary>
    private void EnsureWindhawkRunning(InstallContext context)
    {
        if (runner.FindProcessIds("windhawk").Count > 0)
            return;

        var windhawkPath = resolver.Resolve("windhawk");
        if (windhawkPath is null)
        {
            logger?.LogWarning("windhawk.exe não encontrado — mods configurados, mas o app não foi iniciado.");
            return;
        }

        runner.StartDetached(windhawkPath, "");
        logger?.LogInformation("Windhawk iniciado para carregar os mods recém-configurados.");
        context.Report("Windhawk iniciado.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup — arquivo temporário, sem impacto funcional se sobrar.
        }
    }
}
