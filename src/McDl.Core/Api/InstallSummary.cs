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

        /// <summary>安装结束后的全量校验结果；没跑校验（比如下载就失败了）时为 null。</summary>
        public VerificationReport Verification { get; internal set; }

        /// <summary>
        /// 校验是否算通过。与 <see cref="Succeeded"/> 分开：校验不符说明文件可能被改过，
        /// 但文件确实都在盘上，下载本身是成功的，提示用户即可，不该让调用方把安装当失败重来。
        /// </summary>
        public bool Verified => Verification == null || Verification.Clean;

        public bool Succeeded => Error == null && (Stats != null && Stats.FailedFiles == 0) &&
                                 (Failures == null || Failures.Count == 0);
    }
}
