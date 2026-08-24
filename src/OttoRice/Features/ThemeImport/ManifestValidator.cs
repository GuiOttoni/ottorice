using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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

            if (!IsSafeRelativeSource(target.Source))
                errors.Add($"{label}: source '{target.Source}' deve ser um caminho relativo dentro do repo do tema.");
        }

        return errors;
    }

    public static Result<RiceManifest> Parse(string json)
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
            return Result<RiceManifest>.Fail($"Manifesto não é um JSON válido: {ex.Message}");
        }

        if (manifest is null)
            return Result<RiceManifest>.Fail("Manifesto vazio.");

        var errors = Validate(manifest);
        return errors.Count == 0
            ? Result<RiceManifest>.Ok(manifest)
            : Result<RiceManifest>.Fail(string.Join(Environment.NewLine, errors));
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
