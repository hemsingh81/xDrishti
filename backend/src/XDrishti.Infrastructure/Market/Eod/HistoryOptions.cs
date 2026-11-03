using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace XDrishti.Infrastructure.Market.Eod;

/// <summary>The settings <c>Data:History:*</c> of the history slice (CF-04 Part A), bounds checked at start-up. The fetch is OFF by default.</summary>
internal sealed class HistoryOptions
{
    public const string SectionName = "Data:History";

    /// <summary>Where the probe writes the raw files (<c>Data:History:ProbeFolder</c>). The default is relative to the working folder; a container sets an absolute, mounted, 0700 folder (<c>/app</c> is read-only).</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(300)]
    public string ProbeFolder { get; set; } = "data/history-probe";

    [Required]
    [ValidateObjectMembers]
    public NseOptions Nse { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public QualityOptions Quality { get; set; } = new();

    internal sealed class NseOptions
    {
        /// <summary><c>Data:History:Nse:Enabled</c>, default false: with it off the job and the probe make no request at all.</summary>
        public bool Enabled { get; set; }

        /// <summary>The series that are stored (default EQ).</summary>
        [Required]
        [MinLength(1)]
        public string[] Series { get; set; } = ["EQ"];

        /// <summary>Empty = the client's own User-Agent. A refusal by NSE is a failed run; it is never worked around.</summary>
        [MaxLength(200)]
        public string UserAgent { get; set; } = string.Empty;

        [Range(1, 10_000)]
        public int MaxPerDay { get; set; } = 2_000;

        /// <summary>The wait between two requests, in milliseconds.</summary>
        [Range(250, 60_000)]
        public int DelayMs { get; set; } = 1_500;

        [Range(1, 30)]
        public int Years { get; set; } = 10;
    }

    internal sealed class QualityOptions
    {
        [Range(0.1, 50)]
        public double DailyVolumeTolerancePct { get; set; } = 2;
    }
}
