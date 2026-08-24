using System.Threading;
using System.Threading.Tasks;

namespace OttoRice.AppRegistry.Appliers;

/// <summary>Aplica um arquivo de configuração do tema (já baixado localmente) no alvo resolvido pelo registry.</summary>
public interface IConfigApplier
{
    Task ApplyAsync(string sourcePath, string targetPath, CancellationToken ct = default);
}
