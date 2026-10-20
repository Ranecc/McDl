using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>一种可安装的东西（原版版本、整合包……）。</summary>
    /// <remarks>
    /// 新来源只需要一个实现，再在 <see cref="Installer"/> 的字典里登记；
    /// 下载引擎、CLI、UI 都不用改。示例见 docs/architecture.md「扩展点」。
    /// </remarks>
    public interface IInstallSource
    {
        /// <summary>日志和统计里用的稳定标识（"vanilla"、"modpack"）。</summary>
        string Kind { get; }

        /// <summary>把请求变成可执行的计划；不能下载 payload 文件。</summary>
        Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct);
    }
}
