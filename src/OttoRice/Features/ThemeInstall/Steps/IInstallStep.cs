using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.Features.ThemeInstall.Steps;

/// <summary>
/// Etapa do pipeline transacional. Em falha, o pipeline chama CompensateAsync
/// das etapas executadas em ordem reversa (saga simples, in-process).
/// </summary>
public interface IInstallStep
{
    string Name { get; }

    Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default);

    /// <summary>Desfaz o efeito da etapa. Não recebe o token do pipeline: compensação roda mesmo após cancelamento.</summary>
    Task CompensateAsync(InstallContext context) => Task.CompletedTask;
}
