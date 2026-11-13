using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Feed;

/// <summary>
/// The numbers of the options modules (<c>Options:*</c>, P14-02, documented in doc 16), bounds-checked at start. <see cref="Chain"/> is the option-chain recorder; its <c>Enabled</c> is the deployment gate and is <b>false</b> by default: with it false the
/// recorder never reads credentials, builds a request or sends (it takes a restart to change). The owner's stored switch and the paused service are the other two gates. <see cref="MinFreeGb"/> is the free-space floor of the options recorders.
/// </summary>
internal sealed class OptionsOptions
{
    public const string SectionName = "Options";

    /// <summary>The free disk space under which an options recorder stops (GiB).</summary>
    [Range(5, 500)]
    public int MinFreeGb { get; set; } = 20;

    [Required]
    [ValidateObjectMembers]
    public ChainOptions Chain { get; set; } = new();

    [Required]
    [ValidateObjectMembers]
    public BackfillOptions Backfill { get; set; } = new();

    public OptionChainSettings ToChainSettings() => Chain.ToSettings(MinFreeGb);

    public OptionBackfillSettings ToBackfillSettings() => Backfill.ToSettings(MinFreeGb);
}

/// <summary>An index and how many of its expiries the recorder asks (a bound array item of <c>Options:Chain:IndexExpiries</c>).</summary>
internal sealed class ChainExpiryRuleOptions
{
    [Required]
    [RegularExpression("^NSE:[A-Z0-9_&-]{1,40}$")]
    public string Symbol { get; set; } = string.Empty;

    [Range(0, 6)]
    public int Weekly { get; set; }

    [Range(0, 6)]
    public int Monthly { get; set; }
}

/// <summary><c>Options:Chain:*</c>.</summary>
internal sealed class ChainOptions
{
    /// <summary>The deployment gate (<c>Options__Chain__Enabled</c>).</summary>
    public bool Enabled { get; set; }

    [Range(5, 60)]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>The gap between any two requests of the process; never below 3000 (Dhan: one unique request every 3 seconds).</summary>
    [Range(3000, 60000)]
    public int SpacingMs { get; set; } = 3000;

    [Range(5, 50)]
    public int StrikesEachSide { get; set; } = 30;

    [Range(3, 30)]
    public int StockStrikesEachSide { get; set; } = 15;

    /// <summary>Which index expiries are asked each slot. Empty means the default: NIFTY the next 3 weekly and the next monthly, BANKNIFTY the next 2 monthly (a bound list adds to a default list, so the default lives in <see cref="OptionChainSettings"/>).</summary>
    [Required]
    [ValidateEnumeratedItems]
    public IList<ChainExpiryRuleOptions> IndexExpiries { get; set; } = [];

    [Range(1, 3)]
    public int StockExpiries { get; set; } = 2;

    [Range(1, 15)]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>The stored size of <c>options.chain_snapshots</c> above which recording stops (GiB; the app never deletes data).</summary>
    [Range(1, 100)]
    public int MaxStoredGb { get; set; } = 4;

    public OptionChainSettings ToSettings(int minFreeGb) => new(
        Enabled,
        IntervalMinutes,
        TimeSpan.FromMilliseconds(Math.Max(SpacingMs, (int)OptionChainSettings.MinSpacing.TotalMilliseconds)),
        StrikesEachSide,
        StockStrikesEachSide,
        IndexExpiries.Count == 0 ? OptionChainSettings.DefaultIndexExpiries : [.. IndexExpiries.Select(r => new ChainExpiryRule(r.Symbol, r.Weekly, r.Monthly))],
        StockExpiries,
        TimeSpan.FromSeconds(TimeoutSeconds),
        MaxStoredGb * OptionChainSettings.Gb,
        minFreeGb * OptionChainSettings.Gb);
}

/// <summary><c>Options:Backfill:*</c> (P14-03).</summary>
internal sealed class BackfillOptions
{
    /// <summary>The deployment gate (<c>Options__Backfill__Enabled</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>Requests a second: 0.2 to 2 (Dhan's Data limit is 5 a second, shared with the day's calls).</summary>
    [Range(0.2, 2.0)]
    public double RequestsPerSecond { get; set; } = 1;

    [Range(1000, 90000)]
    public int MaxRequestsPerDay { get; set; } = 40000;

    [Range(1, 5)]
    public int Years { get; set; } = 5;

    [Range(0, 30)]
    public int StockCount { get; set; } = 20;

    [Range(0, 10)]
    public int IndexOffsets { get; set; } = 10;

    [Range(0, 3)]
    public int StockOffsets { get; set; } = 3;

    [Range(5, 60)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>The stored size of <c>options.rolling_bars</c> above which fetching stops (GiB; the app never deletes data).</summary>
    [Range(1, 50)]
    public int MaxStoredGb { get; set; } = 3;

    public OptionBackfillSettings ToSettings(int minFreeGb) => new(
        Enabled,
        RequestsPerSecond,
        MaxRequestsPerDay,
        Years,
        StockCount,
        IndexOffsets,
        StockOffsets,
        TimeSpan.FromSeconds(TimeoutSeconds),
        MaxStoredGb * OptionBackfillSettings.Gb,
        minFreeGb * OptionBackfillSettings.Gb);
}
