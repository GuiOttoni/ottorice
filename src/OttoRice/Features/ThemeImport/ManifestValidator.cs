using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry;
using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeImport;

/// <summary>
/// Validação estrita do manifesto v1 (falha rápida, RF-02). Além da forma, aplica as
/// regras de segurança: app na whitelist, ação permitida para o app e source
/// obrigatoriamente relativo ao repo do tema (sem path traversal).
/// </summary>
public static partial class ManifestValidator
{
    public const string SupportedSchemaVersion = "1.0";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ThemeIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\.\-\+_]*$")]
    private static partial Regex WingetIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.\[\]-]+$")]
    private static partial Regex WindhawkSettingsKeyPattern();

    public static IReadOnlyList<string> Validate(RiceManifest manifest)
    {
        var errors = new List<string>();

        if (manifest.SchemaVersion != SupportedSchemaVersion)
            errors.Add($"schemaVersion '{manifest.SchemaVersion}' não suportada (esperado '{SupportedSchemaVersion}').");

        if (string.IsNullOrWhiteSpace(manifest.ThemeId) || !ThemeIdPattern().IsMatch(manifest.ThemeId))
            errors.Add("themeId obrigatório, em kebab-case (ex.: 'blackturq-minimal').");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("name é obrigatório.");

        if (manifest.Targets.Count == 0)
            errors.Add("O manifesto precisa de ao menos um target.");

        foreach (var dep in manifest.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dep.WingetId) || !WingetIdPattern().IsMatch(dep.WingetId))
                errors.Add($"Dependência com wingetId inválido: '{dep.WingetId}'.");
        }

        for (var i = 0; i < manifest.Targets.Count; i++)
        {
            var target = manifest.Targets[i];
            var label = $"targets[{i}]";

            if (string.IsNullOrWhiteSpace(target.App) || !SupportedApps.IsSupported(target.App))
            {
                errors.Add($"{label}: app '{target.App}' não é suportado.");
                continue;
            }

            var definition = SupportedApps.All[target.App];
            if (string.IsNullOrWhiteSpace(target.Action) || !definition.AllowedActions.Contains(target.Action))
                errors.Add($"{label}: ação '{target.Action}' não permitida para '{target.App}' " +
                           $"(permitidas: {string.Join(", ", definition.AllowedActions)}).");

            // "configure_mod" (mods Windhawk): "source" é opcional — um YAML com os settings
            // do mod no mesmo formato da UI do Windhawk (ver WindhawkSettingsFlattener),
            // achatado em pares chave/valor. "settings" inline continua aceito, some/mescla
            // com o que vier do YAML. Sem nenhum dos dois, só instala/habilita o mod com os
            // valores default (ou os que o usuário já tiver escolhido na galeria do mod).
            if (target.Action == "configure_mod")
            {
                if (!string.IsNullOrWhiteSpace(target.Source) && !IsSafeRelativeSource(target.Source))
                    errors.Add($"{label}: source '{target.Source}' deve ser um caminho relativo dentro do repo do tema.");

                foreach (var (key, value) in target.Settings ?? [])
                {
                    if (!WindhawkSettingsKeyPattern().IsMatch(key))
                        errors.Add($"{label}: chave de settings inválida: '{key}'.");
                    // Sem denylist de caracteres: a execução (ConfigureWindhawkModsStep) passa
                    // cada valor como literal de string do PowerShell (aspas simples, só ' é
                    // escapado), não interpolado numa linha de shell — CSS/JS de verdade
                    // (com &, |, ", %, quebras de linha) precisa passar sem ser bloqueado.
                    if ((value ?? "").Contains('\0'))
                        errors.Add($"{label}: valor de settings['{key}'] contém byte nulo.");
                    if ((value ?? "").Length > 100_000)
                        errors.Add($"{label}: valor de settings['{key}'] excede o tamanho máximo (100.000 caracteres).");
                }
            }
            else if (!IsSafeRelativeSource(target.Source))
            {
                errors.Add($"{label}: source '{target.Source}' deve ser um caminho relativo dentro do repo do tema.");
            }
        }

        return errors;
    }

    public static Result<RiceManifest> Parse(string json, ILogger? logger = null)
    {
        RiceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RiceManifest>(json, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Manifesto não é um JSON válido.");
            return Result<RiceManifest>.Fail($"Manifesto não é um JSON válido: {ex.Message}");
        }

        if (manifest is null)
            return Result<RiceManifest>.Fail("Manifesto vazio.");

        var errors = Validate(manifest);
        if (errors.Count > 0)
        {
            logger?.LogWarning(
                "Manifesto '{ThemeId}' reprovado na validação: {Errors}",
                manifest.ThemeId, string.Join(" | ", errors));
            return Result<RiceManifest>.Fail(string.Join(Environment.NewLine, errors));
        }

        return Result<RiceManifest>.Ok(manifest);
    }

    private static bool IsSafeRelativeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        if (Path.IsPathRooted(source) || source.Contains(':'))
            return false;

        foreach (var segment in source.Split('/', '\\'))
        {
            if (segment == "..")
                return false;
        }
        return true;
    }
}
