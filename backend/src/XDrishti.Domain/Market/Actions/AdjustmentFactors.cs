namespace XDrishti.Domain.Market.Actions;

/// <summary>
/// The cumulative backward adjustment of one instrument's prices for its <see cref="CorporateActionStatus.Applied"/> actions (ADR 0017 §5). Pure.
/// <list type="bullet">
/// <item>A bar dated strictly <em>before</em> an action's ex-date is multiplied by the action's factor; a bar on the ex-date or later is not. Several actions multiply (order does not matter).</item>
/// <item>Prices (open, high, low, close) are multiplied by <see cref="FactorAt"/> and rounded to <see cref="PriceDecimals"/> decimals — only at the output, never in between.
/// Volume is divided by the factor (more shares before a split) and rounded to whole shares.</item>
/// <item>The date of a bar is its India date. A minute, intraday or day bar lies within one day, so its factor is exact. A week or month bucket that contains an ex-date after its first day has two factors:
/// the reader builds it again from the adjusted day bars (see GetInstrumentBars); a bucket without one is adjusted by the factor of its first day, which is the factor of all its days.</item>
/// </list>
/// </summary>
public sealed class AdjustmentFactors
{
    public const int PriceDecimals = 4;

    private readonly AppliedAction[] _actions;

    private readonly double[] _suffix;

    // Source-aware mode (CF-04d): null for the plain rule. A bar dated before _seam is Dhan's raw backfill and takes every action after it; a bar at or after it is a CSV bar, already adjusted at source through _through.
    private readonly DateOnly? _seam;

    private readonly DateOnly? _csvAdjustedThrough;

    private AdjustmentFactors(AppliedAction[] actions, DateOnly? seam = null, DateOnly? csvAdjustedThrough = null)
    {
        _seam = seam;
        _csvAdjustedThrough = csvAdjustedThrough;
        _actions = actions;
        _suffix = new double[actions.Length + 1];
        _suffix[actions.Length] = 1.0;
        for (var i = actions.Length - 1; i >= 0; i--)
        {
            _suffix[i] = _suffix[i + 1] * (double)actions[i].Factor;
        }
    }

    public static AdjustmentFactors None { get; } = new([]);

    /// <summary>Every applied action, by ex-date ascending.</summary>
    public IReadOnlyList<AppliedAction> Actions => _actions;

    public static AdjustmentFactors From(IEnumerable<AppliedAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var sorted = actions.OrderBy(a => a.ExDate).ThenBy(a => a.Kind).ToArray();
        return sorted.Length == 0 ? None : new AdjustmentFactors(sorted);
    }

    /// <summary>
    /// The same actions in source-aware mode (CF-04d, ADR 0273): <see cref="FactorAt"/> then follows <see cref="XDrishti.Domain.Market.History.SourceAwareFactors"/>, the oracle. A bar dated before <paramref name="seamDate"/> takes every action whose ex-date is after it; a bar at or after the seam is a CSV bar
    /// that is already adjusted through <paramref name="csvAdjustedThrough"/> and takes the actions whose ex-date is after both its date and that date. <see cref="Actions"/> and <see cref="ActionsAfter"/> are unchanged.
    /// </summary>
    public AdjustmentFactors SourceAware(DateOnly seamDate, DateOnly? csvAdjustedThrough) => _actions.Length == 0 ? this : new AdjustmentFactors(_actions, seamDate, csvAdjustedThrough);

    /// <summary>The factor for a bar dated <paramref name="barDate"/>: the product of the factors of all actions whose ex-date is after it (1 when none); in source-aware mode, see <see cref="SourceAware"/>.</summary>
    public double FactorAt(DateOnly barDate)
    {
        if (_seam is { } seam && barDate >= seam && _csvAdjustedThrough is { } through && through > barDate)
        {
            return _suffix[FirstAfter(through)];
        }

        return _suffix[FirstAfter(barDate)];
    }

    /// <summary>The actions that change the bars dated <paramref name="barDate"/> (ex-date after it), by ex-date ascending.</summary>
    public IReadOnlyList<AppliedAction> ActionsAfter(DateOnly barDate)
    {
        var first = FirstAfter(barDate);
        return first == 0 ? _actions : _actions[first..];
    }

    public static double Price(double price, double factor) => Math.Round(price * factor, PriceDecimals, MidpointRounding.AwayFromZero);

    public static long Volume(long volume, double factor) => factor == 1.0 ? volume : (long)Math.Round(volume / factor, MidpointRounding.AwayFromZero);

    // The index of the first action with an ex-date after the date (binary search; the actions are sorted).
    private int FirstAfter(DateOnly date)
    {
        var low = 0;
        var high = _actions.Length;
        while (low < high)
        {
            var mid = (low + high) >>> 1;
            if (_actions[mid].ExDate > date)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }
}
