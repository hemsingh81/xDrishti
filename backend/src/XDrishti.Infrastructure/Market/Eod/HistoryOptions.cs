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

    [Required]
    [ValidateObjectMembers]
    public BackfillOptions Backfill { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public ProbeOptions Probe { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public ReplayOptions Replay { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public ListsOptions Lists { get; set; } = new();

    /// <summary>The dated ban-list archive (DATA-01, ADR 0274). OFF by default.</summary>
    internal sealed class ListsOptions
    {
        /// <summary><c>Data:History:Lists:Enabled</c>, default false: with it off the probe and the backfill make no request at all.</summary>
        public bool Enabled { get; set; }

        [Range(1, 10_000)]
        public int MaxPerDay { get; set; } = 500;

        /// <summary>The wait between two requests, in milliseconds.</summary>
        [Range(250, 60_000)]
        public int DelayMs { get; set; } = 1_500;
    }

    /// <summary>What a simulation does with backfilled pre-seam bars (CF-04d, ADR 0273).</summary>
    internal sealed class ReplayOptions
    {
        /// <summary><c>Data:History:Replay:AdmitVerified</c>, default false. On: the pre-seam bars of an instrument that is proven verified enter a run, adjusted by the seam rule. Flip it between jobs, not during one.</summary>
        public bool AdmitVerified { get; set; }
    }

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

    /// <summary>The 1-minute backfill (CF-04 Part B). OFF by default: with it off nothing is requested.</summary>
    internal sealed class BackfillOptions
    {
        /// <summary><c>Data:History:Backfill:Enabled</c>, default false.</summary>
        public bool Enabled { get; set; }

        [Range(1, 5)]
        public int Years { get; set; } = 5;

        [Range(7, 90)]
        public int ChunkDays { get; set; } = 90;

        [Range(1, 10)]
        public int MaxRetries { get; set; } = 3;

        [Range(1, 10)]
        public int DiskHeadroomFactor { get; set; } = 2;

        [Range(1, 1_000)]
        public int BytesPerRowEstimate { get; set; } = 60;

        /// <summary>The off-hours window of the scheduled run, <c>HH:mm-HH:mm</c> India time (it may cross midnight).</summary>
        [Required]
        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d-([01]\d|2[0-3]):[0-5]\d$", MatchTimeoutInMilliseconds = 1000)]
        public string RunWindow { get; set; } = "20:00-06:00";
    }

    /// <summary>The overlap probe of the seam (CF-04 Part B, decision 9).</summary>
    internal sealed class ProbeOptions
    {
        [Range(1, 500)]
        public int Instruments { get; set; } = 20;

        [Range(1, 30)]
        public int Sessions { get; set; } = 10;

        [Range(0, 1_000)]
        public double MedianBps { get; set; } = 5;

        [Range(0, 10_000)]
        public double MaxBps { get; set; } = 50;

        [Range(50, 100)]
        public double MinWithinMaxPct { get; set; } = 99;
    }
}
