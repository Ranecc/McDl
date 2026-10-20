using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>
    /// One kind of installable payload (vanilla version, modpack, ...).
    /// </summary>
    /// <remarks>
    /// Adding a new source means adding one implementation here and registering it in
    /// <see cref="Installer"/>; nothing in the engine, the CLI or the UI has to change.
    /// </remarks>
    public interface IInstallSource
    {
        /// <summary>Stable identifier used in logs ("vanilla", "modpack").</summary>
        string Kind { get; }

        /// <summary>Turns a request into a ready to run plan. Must not download payload files.</summary>
        Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct);
    }
}
