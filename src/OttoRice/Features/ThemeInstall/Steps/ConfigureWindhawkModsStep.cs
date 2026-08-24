using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Instala/configura mods do Windhawk (ação "configure_mod") via `windhawk-cli`.
/// Melhor esforço, nunca derruba o pipeline: Windhawk é pré-requisito manual (não é
/// instalado pelo OttoRice — só a build 2.0 alpha tem o CLI, ainda fora do WinGet), e
/// escrita no windhawk-cli exige elevação (UAC). Por isso todas as chamadas de um mesmo
/// tema são batidas num único script elevado — um prompt de UAC só, não um por mod.
/// </summary>
public sealed partial class ConfigureWindhawkModsStep(
    IExecutableResolver resolver,
    IProcessRunner runner,
    ILogger<ConfigureWindhawkModsStep>? logger = null) : IInstallStep
{
    // Defesa em profundidade: o ManifestValidator já barra isso, mas um valor de settings
    // vira texto interpolado num .cmd rodado ELEVADO — nunca escrever aqui sem checar de
    // novo, mesmo raciocínio do TargetPlanner pro path traversal.
    [GeneratedRegex(@"^[A-Za-z0-9_.\[\]-]+$")]
    private static partial Regex SafeKeyPattern();

    [GeneratedRegex("""[&|<>^"%\r\n\x00]""")]
    private static partial Regex UnsafeValueCharsPattern();

    public string Name => "Mods do Windhawk";

    public async Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
    {
        var modOps = context.Operations.Where(op => op.Target.Action == "configure_mod").ToList();
        if (modOps.Count == 0)
            return Result.Ok();

        foreach (var op in modOps)
        {
            foreach (var (key, value) in op.Target.Settings ?? [])
            {
                if (!SafeKeyPattern().IsMatch(key) || UnsafeValueCharsPattern().IsMatch(value ?? ""))
                {
                    logger?.LogError(
                        "Settings de '{App}' com valor não seguro para o comando elevado — abortando os mods do Windhawk.",
                        op.Target.App);
                    context.Report("⚠ Settings de mod do Windhawk com caractere não permitido — mods não configurados.");
                    return Result.Ok();
                }
            }
        }

        var cliPath = resolver.Resolve("windhawk-cli");
        if (cliPath is null)
        {
            logger?.LogWarning("windhawk-cli não encontrado — pulando {Count} mod(s) do Windhawk.", modOps.Count);
            context.Report(
                "⚠ Windhawk não encontrado — pulando os mods do tema. " +
                "Instale manualmente em https://windhawk.net/ (build 2.0 ou mais recente) e reaplique o tema.");
            return Result.Ok();
        }

        var logPath = Path.GetTempFileName();
        var batchPath = Path.ChangeExtension(logPath, ".cmd");
        try
        {
            File.WriteAllText(batchPath, BuildBatchScript(cliPath, logPath, modOps.Select(op => op.Target)));

            context.Report($"Configurando {modOps.Count} mod(s) do Windhawk (pode pedir confirmação do UAC)...");
            var exitCode = await runner.RunElevatedAsync("cmd.exe", $"/c \"{batchPath}\"", ct);

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
                    string.Join(", ", modOps.Select(op => op.Target.App)));
                context.Report("Mods do Windhawk configurados.");
                EnsureWindhawkRunning(context);
            }
        }
        finally
        {
            TryDelete(batchPath);
            TryDelete(logPath);
        }

        // Melhor esforço: nunca falha o pipeline (Windhawk é opcional/manual).
        return Result.Ok();
    }

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

    private static string BuildBatchScript(
        string cliPath, string logPath, IEnumerable<Features.ThemeImport.Models.RiceTarget> targets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        foreach (var target in targets)
        {
            sb.AppendLine($"\"{cliPath}\" mod install \"{target.App}\" >> \"{logPath}\" 2>&1");
            sb.AppendLine("if errorlevel 1 exit /b 1");

            if (target.Settings is { Count: > 0 })
            {
                var pairs = string.Join(" ", target.Settings.Select(kv => $"\"{kv.Key}={kv.Value}\""));
                sb.AppendLine($"\"{cliPath}\" mod settings set \"{target.App}\" {pairs} >> \"{logPath}\" 2>&1");
                sb.AppendLine("if errorlevel 1 exit /b 1");
            }
        }
        sb.AppendLine("exit /b 0");
        return sb.ToString();
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
