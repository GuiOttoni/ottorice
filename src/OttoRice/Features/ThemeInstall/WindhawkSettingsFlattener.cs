using System.Collections.Generic;
using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace OttoRice.Features.ThemeInstall;

/// <summary>
/// Converte o YAML de settings de um mod do Windhawk (o mesmo formato que a própria UI do
/// Windhawk mostra no "modo textual", ex.: `theme: Down Aero`) para os pares chave/valor
/// no formato "flat storage" que o `windhawk-cli mod settings set` espera
/// (`controlStyles[0].target=...`). Listas viram `chave[índice]`, objetos viram
/// `chave.subchave`, folha vira `chave=valor` (escalar nulo/vazio vira string vazia).
/// </summary>
public static class WindhawkSettingsFlattener
{
    public static IReadOnlyDictionary<string, string> Flatten(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new System.IO.StringReader(yaml));

        var result = new Dictionary<string, string>();
        if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root)
            FlattenNode(root, prefix: "", result);
        return result;
    }

    private static void FlattenNode(YamlNode node, string prefix, Dictionary<string, string> result)
    {
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (keyNode, valueNode) in map.Children)
                {
                    var key = ((YamlScalarNode)keyNode).Value ?? "";
                    var childPrefix = prefix.Length == 0 ? key : $"{prefix}.{key}";
                    FlattenNode(valueNode, childPrefix, result);
                }
                break;

            case YamlSequenceNode seq:
                for (var i = 0; i < seq.Children.Count; i++)
                    FlattenNode(seq.Children[i], $"{prefix}[{i.ToString(CultureInfo.InvariantCulture)}]", result);
                break;

            case YamlScalarNode scalar:
                result[prefix] = scalar.Value ?? "";
                break;
        }
    }
}
