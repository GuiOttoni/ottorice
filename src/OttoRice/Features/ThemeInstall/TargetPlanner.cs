using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using OttoRice.AppRegistry;
using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Traduz os targets do manifesto (validado) em operações concretas de arquivo.
/// Os caminhos de destino vêm exclusivamente do SupportedApps + locator — nunca do manifesto.
/// </summary>
public sealed class TargetPlanner(
    WindowsTerminalLocator wtLocator,
    Func<string, string>? pathExpander = null,
    ILogger<TargetPlanner>? logger = null)
{
    private readonly Func<string, string> _expand = pathExpander ?? PathResolver.Expand;

    /// <param name="targets">
    /// Targets a planejar — por padrão (<c>null</c>), todos os do manifesto. Passar um
    /// subconjunto é como o toggle por componente (RF) filtra o que chega a ser planejado/
    /// aplicado, sem precisar de um manifesto "reduzido" nem reabrir a validação.
    /// </param>
    public Result<List<FileOperation>> Build(
        RiceManifest manifest, string themeDirectory, IReadOnlyList<RiceTarget>? targets = null)
    {
        var themeRoot = Path.GetFullPath(themeDirectory);
        var operations = new List<FileOperation>();

        foreach (var target in targets ?? manifest.Targets)
        {
            var app = SupportedApps.All[target.App!];

            // Mods Windhawk não copiam arquivo pra lugar nenhum (por isso TargetPath fica
            // vazio) — mas o "source", se houver, é um YAML de settings que o
            // ConfigureWindhawkModsStep lê e achata (WindhawkSettingsFlattener). Sem
            // "source", só os "settings" inline do target (se houver) são usados.
            if (target.Action == "configure_mod")
            {
                if (string.IsNullOrWhiteSpace(target.Source))
                {
                    operations.Add(new FileOperation(target, SourcePath: "", TargetPath: ""));
                    continue;
                }

                var modSourcePath = Path.GetFullPath(Path.Combine(themeRoot, target.Source));
                if (!modSourcePath.StartsWith(themeRoot, StringComparison.OrdinalIgnoreCase))
                    return Fail($"source '{target.Source}' resolve para fora do diretório do tema.");
                if (!File.Exists(modSourcePath))
                    return Fail($"settings '{target.Source}' não encontrado no tema.");

                operations.Add(new FileOperation(target, modSourcePath, TargetPath: ""));
                continue;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(themeRoot, target.Source!));

            // Defesa em profundidade: o validator já barra "..", mas nunca opere fora do tema.
            if (!sourcePath.StartsWith(themeRoot, StringComparison.OrdinalIgnoreCase))
                return Fail($"source '{target.Source}' resolve para fora do diretório do tema.");

            switch (target.Action)
            {
                case "set":
                    if (!File.Exists(sourcePath))
                        return Fail($"wallpaper '{target.Source}' não encontrado no tema.");
                    operations.Add(new FileOperation(target, sourcePath, TargetPath: ""));
                    break;

                case "merge_scheme":
                    if (!File.Exists(sourcePath))
                        return Fail($"esquema '{target.Source}' não encontrado no tema.");
                    var wtSettings = wtLocator.FindSettingsPath();
                    if (wtSettings is null)
                        return Fail("Windows Terminal não encontrado (nem Store nem unpackaged).");
                    operations.Add(new FileOperation(target, sourcePath, wtSettings));
                    break;

                case "override" when Directory.Exists(sourcePath):
                    if (app.ConfigRoot is null)
                        return Fail($"'{app.Id}' não aceita pasta como source.");
                    var root = _expand(app.ConfigRoot);
                    foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(sourcePath, file);
                        operations.Add(new FileOperation(target, file, Path.Combine(root, relative)));
                    }
                    break;

                case "override" when File.Exists(sourcePath):
                    // Só casa por nome de arquivo exato — nunca "adivinha" caindo no primeiro
                    // ConfigPaths conhecido: um arquivo de tema mal nomeado sobrescreveria o
                    // config errado do app silenciosamente.
                    var fileName = Path.GetFileName(sourcePath);
                    var match = app.ConfigPaths.FirstOrDefault(p =>
                        Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    if (match is null && app.ConfigRoot is not null)
                    {
                        operations.Add(new FileOperation(
                            target, sourcePath, Path.Combine(_expand(app.ConfigRoot), fileName)));
                        break;
                    }
                    if (match is null)
                        return Fail($"'{app.Id}' não tem caminho de config conhecido para '{fileName}' " +
                                    $"(esperado: {string.Join(", ", app.ConfigPaths.Select(Path.GetFileName))}).");
                    operations.Add(new FileOperation(target, sourcePath, _expand(match)));
                    break;

                case "override":
                    return Fail($"source '{target.Source}' não encontrado no tema.");

                default:
                    return Fail($"ação '{target.Action}' sem planner (registry e validator divergiram).");
            }
        }

        logger?.LogInformation(
            "Plano gerado para o tema '{ThemeId}': {Count} operação(ões).", manifest.ThemeId, operations.Count);
        return Result<List<FileOperation>>.Ok(operations);

        Result<List<FileOperation>> Fail(string error)
        {
            logger?.LogWarning("Planejamento do tema '{ThemeId}' falhou: {Error}", manifest.ThemeId, error);
            return Result<List<FileOperation>>.Fail(error);
        }
    }
}
