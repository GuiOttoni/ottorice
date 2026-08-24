using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Appliers;

/// <summary>
/// Merge cirúrgico no settings.json do Windows Terminal: faz upsert do esquema de cores
/// em schemes[] e (opcional) o define em profiles.defaults.colorScheme. Nunca sobrescreve
/// o restante do arquivo (atalhos, perfis WSL etc.).
/// Limitação conhecida: o arquivo pode conter comentários (JSONC); eles são aceitos na
/// leitura mas descartados na escrita.
/// </summary>
public sealed class WindowsTerminalApplier(ILogger<WindowsTerminalApplier>? logger = null)
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task InjectColorSchemeAsync(
        string targetPath, string schemeJsonContent, bool setAsDefault, CancellationToken ct = default)
    {
        if (!File.Exists(targetPath))
            throw new FileNotFoundException($"settings.json do Windows Terminal não encontrado: {targetPath}");

        var rootNode = JsonNode.Parse(await File.ReadAllTextAsync(targetPath, ct), null, ParseOptions) as JsonObject
            ?? throw new InvalidOperationException("O settings.json do Windows Terminal está malformado.");

        var newScheme = JsonNode.Parse(schemeJsonContent, null, ParseOptions) as JsonObject
            ?? throw new ArgumentException("O esquema de cores do tema não é um objeto JSON válido.");

        var schemeName = newScheme["name"]?.GetValue<string>()
            ?? throw new ArgumentException("O esquema de cores precisa da propriedade 'name'.");

        if (rootNode["schemes"] is not JsonArray schemes)
        {
            schemes = [];
            rootNode["schemes"] = schemes;
        }

        // Upsert por nome. DeepClone: um JsonNode não pode ter dois pais.
        var existing = schemes.FirstOrDefault(s => s?["name"]?.GetValue<string>() == schemeName);
        if (existing is not null)
            schemes[schemes.IndexOf(existing)] = newScheme.DeepClone();
        else
            schemes.Add(newScheme.DeepClone());

        if (setAsDefault)
        {
            if (rootNode["profiles"] is not JsonObject profiles)
            {
                profiles = [];
                rootNode["profiles"] = profiles;
            }
            if (profiles["defaults"] is not JsonObject defaults)
            {
                defaults = [];
                profiles["defaults"] = defaults;
            }
            defaults["colorScheme"] = schemeName;
        }

        var json = rootNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await AtomicFileWriter.WriteAllTextAsync(targetPath, json, ct, logger);
        logger?.LogInformation(
            "Esquema '{SchemeName}' injetado em '{TargetPath}' (padrão: {SetAsDefault}).",
            schemeName, targetPath, setAsDefault);
    }
}
