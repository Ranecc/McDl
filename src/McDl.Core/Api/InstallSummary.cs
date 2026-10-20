using System.Collections.Generic;

namespace McDl.Core
{
    /// <summary>一次安装的结果，CLI 和 UI 都按这个结构展示。</summary>
    public sealed class InstallSummary
    {
        public InstallPlan Plan { get; set; }

        /// <summary>传输统计；下载开始前就失败时为 null。</summary>
        public TransferStats Stats { get; set; }

        /// <summary>没能落盘的文件及首个错误。</summary>
        public IReadOnlyList<string> Failures { get; set; }

        /// <summary>整体中断的原因（不是单个文件失败时）。</summary>
        public string Error { get; set; }

        public bool Succeeded => Error == null && (Stats != null && Stats.FailedFiles == 0) &&
                                 (Failures == null || Failures.Count == 0);
    }
}
