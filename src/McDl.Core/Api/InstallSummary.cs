using System.Collections.Generic;

namespace McDl.Core
{
    /// <summary>Outcome of one install run, in a shape both the CLI and the UI can print.</summary>
    public sealed class InstallSummary
    {
        public InstallPlan Plan { get; set; }

        /// <summary>Transfer counters; null when the run failed before the download phase.</summary>
        public TransferStats Stats { get; set; }

        /// <summary>Every file that could not be placed, with its first error.</summary>
        public IReadOnlyList<string> Failures { get; set; }

        /// <summary>Set when the run aborted for a reason other than individual files.</summary>
        public string Error { get; set; }

        public bool Succeeded => Error == null && (Stats != null && Stats.FailedFiles == 0) &&
                                 (Failures == null || Failures.Count == 0);
    }
}
