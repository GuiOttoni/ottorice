using System.Threading;
using System.Threading.Tasks;
using OttoRice.Common;

namespace OttoRice.AppRegistry.Reloaders;

/// <summary>
/// Executa uma ReloadAction da whitelist (fase 2: GlazeWM, YASB, Zebar, wallpaper).
/// O manifesto nunca fornece comandos — só o registry mapeia ação → comando.
/// </summary>
public interface IAppReloader
{
    Task<Result> ReloadAsync(ReloadAction action, CancellationToken ct = default);
}
