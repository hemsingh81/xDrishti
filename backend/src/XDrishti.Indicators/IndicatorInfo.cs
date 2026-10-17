using System.Collections.ObjectModel;
using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// What the DSL validator and the feature frame know about one indicator (ADR 0031): its name, parameters with ranges and defaults, the session behaviour, how many bars it needs before it is ready (a function
/// of the parameters and the timeframe) and a factory. Built only by <see cref="IndicatorCatalog"/>.
/// </summary>
public sealed class IndicatorInfo
{
    private readonly Func<FeatureSpec, int> _warmUp;

    private readonly Func<FeatureSpec, IIndicator> _factory;

    private readonly Func<FeatureSpec, SpecProblem?>? _extraCheck;

    private readonly DateFeatureKind? _dateFeature;

    internal IndicatorInfo(
        string name,
        string summary,
        IReadOnlyList<IndicatorParameter> parameters,
        SessionBehaviour sessionBehaviour,
        bool usesSource,
        PriceSource defaultSource,
        bool intradayOnly,
        Func<FeatureSpec, int> warmUp,
        Func<FeatureSpec, IIndicator> factory,
        Func<FeatureSpec, SpecProblem?>? extraCheck = null,
        DateFeatureKind? dateFeature = null,
        bool requiresContext = false,
        bool requiresExpiryDays = false)
    {
        Name = name;
        Summary = summary;
        Parameters = new ReadOnlyCollection<IndicatorParameter>([.. parameters]);
        SessionBehaviour = sessionBehaviour;
        UsesSource = usesSource;
        DefaultSource = defaultSource;
        IntradayOnly = intradayOnly;
        _warmUp = warmUp;
        _factory = factory;
        _extraCheck = extraCheck;
        _dateFeature = dateFeature;
        RequiresContext = requiresContext;
        RequiresExpiryDays = requiresExpiryDays;

        // Built here, once, from the fixed order of the timeframes: the catalog is shared by the whole process, so nothing in it may be filled in lazily by whichever thread reads first.
        var supported = new List<Timeframe>(IndicatorCatalog.Timeframes.Length);
        foreach (var timeframe in IndicatorCatalog.Timeframes)
        {
            if (Supports(timeframe))
            {
                supported.Add(timeframe);
            }
        }

        SupportedTimeframes = new ReadOnlyCollection<Timeframe>(supported);
    }

    /// <summary>The lower-case snake_case name used in specs and in the DSL.</summary>
    public string Name { get; }

    public string Summary { get; }

    /// <summary>The parameters in the order of <see cref="FeatureSpec.P1"/> … <see cref="FeatureSpec.P4"/> (at most four).</summary>
    public IReadOnlyList<IndicatorParameter> Parameters { get; }

    /// <summary>Whether the indicator runs on across sessions (a rolling window, a trend, a pattern, a raw bar) or restarts at each India trading date; the second kind is refused on 1W and 1M, whose bars span many dates.</summary>
    public SessionBehaviour SessionBehaviour { get; }

    /// <summary>Whether the indicator reads a chosen price (<see cref="FeatureSpec.Source"/>); the others read the whole bar and treat all sources as the same feature.</summary>
    public bool UsesSource { get; }

    public PriceSource DefaultSource { get; }

    /// <summary>Whether the indicator only makes sense on intraday bars (it is refused on 1D, 1W and 1M).</summary>
    public bool IntradayOnly { get; }

    /// <summary>
    /// Whether the feature changes at most once per India date and is asked for on 1m only: the two calendar features (<c>day_of_week</c>, <c>is_expiry_day</c>) and the ten that read the daily
    /// <see cref="MarketContext"/>. The frame keeps them in a lane of their own that is refreshed at the first accepted step of each date and writes their history once per date (ADR 0033); their warm-up is counted
    /// in days of the context (see <see cref="DateFeature"/>). The ones that read the context need <see cref="FrameOptions.Context"/> (<see cref="RequiresContext"/>), <c>is_expiry_day</c> needs <see cref="FrameOptions.ExpiryDays"/> (<see cref="RequiresExpiryDays"/>).
    /// </summary>
    public bool DateDriven => _dateFeature is not null;

    /// <summary>
    /// Whether the feature restarts at each India trading date <b>in the frame</b> (ADR 0035): <c>vwap</c>, <c>or_high</c>, <c>or_low</c>, <c>today_open/high/low</c>, <c>prev_day_open/high/low/close/volume</c> and
    /// <c>gap_pct</c> — the twelve <see cref="SessionBehaviour.PerSession"/> names — on an <b>intraday</b> timeframe (1m, 5m, 10m, 15m, 30m, 1h). For such a feature the frame keeps the value history of the current
    /// session only (<see cref="FeatureFrame.At"/> is unknown for a lag that reaches before the lane's first bar of the date) and reports it unknown at every lag from the start of a new date until its timeframe has closed
    /// its first bar of the date. False for every continuous indicator and every date-driven one, and false on 1D (a daily bar is a whole session: nothing restarts, so a lag is by position), 1W and 1M (refused for these
    /// names, see <see cref="Supports"/>). It is the one place that says which features restart: the frame, the rules language's lint and the tests ask here and do not know the names.
    /// </summary>
    /// <param name="spec">The feature: the same name restarts on 1m to 1h and not on 1D.</param>
    public bool RestartsEachSession(in FeatureSpec spec) => SessionBehaviour == SessionBehaviour.PerSession && spec.Timeframe.Minutes() > 0;

    /// <summary>
    /// Whether a frame that computes this feature must be given <see cref="FrameOptions.Context"/>: true for exactly the ten date-driven features that read the daily <see cref="MarketContext"/>
    /// (<c>index_trend</c>, <c>index_adx</c>, <c>index_vol_pct</c>, <c>vix</c>, <c>vix_pct</c>, <c>vix_chg</c>, <c>breadth</c>, <c>regime_trend</c>, <c>regime_vol</c>, <c>regime</c>), false for every other name
    /// (<c>day_of_week</c> and <c>is_expiry_day</c> read only the date). <see cref="FeatureFrame.Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/> refuses a spec of such a feature without the context; a caller
    /// that compiles conditions (the strategy language, ADR 0034) asks the catalog here instead of knowing which names those are.
    /// </summary>
    public bool RequiresContext { get; }

    /// <summary>
    /// Whether a frame that computes this feature must be given <see cref="FrameOptions.ExpiryDays"/>: true for <c>is_expiry_day</c> only (an empty set is a valid set), false for every other name.
    /// <see cref="FeatureFrame.Create(IReadOnlyList{FeatureSpec}, FrameOptions?)"/> refuses a spec of it without the dates.
    /// </summary>
    public bool RequiresExpiryDays { get; }

    /// <summary>
    /// The timeframes a feature of this indicator may be asked for on, shortest first: every one of <see cref="IndicatorCatalog.SupportedTimeframes"/> for which <see cref="Supports"/> is true (nine for the continuous
    /// indicators; six for the intraday-only ones, seven for the other session-bound ones and only 1m for the date-driven ones). A read-only view, built once with the indicator (immutable, so any thread may read it).
    /// </summary>
    public IReadOnlyList<Timeframe> SupportedTimeframes { get; }

    /// <summary>
    /// Whether a feature of this indicator may be asked for on the timeframe: one of <see cref="IndicatorCatalog.SupportedTimeframes"/>, not an intraday-only indicator (<c>vwap</c>, <c>or_high</c>, <c>or_low</c>) on 1D,
    /// 1W or 1M, and not a per-session indicator (those and <c>prev_day_*</c>, <c>today_*</c>, <c>gap_pct</c>) on 1W or 1M: a weekly or monthly bar has no session to restart at. A date-driven feature
    /// (<see cref="DateDriven"/>) changes once per date whatever the bar, so it is asked for on 1m only. The frame refuses a spec the catalog does not support with an error on <c>specs[i].timeframe</c> that names the
    /// feature and the timeframe.
    /// </summary>
    public bool Supports(Timeframe timeframe) =>
        IndicatorCatalog.IsSupported(timeframe)
        && !(IntradayOnly && timeframe is Timeframe.D1 or Timeframe.W1 or Timeframe.Mo1)
        && !(SessionBehaviour == SessionBehaviour.PerSession && timeframe is Timeframe.W1 or Timeframe.Mo1)
        && !(DateDriven && timeframe != Timeframe.M1);

    /// <summary>
    /// The bars of the spec's own timeframe after which the indicator is first ready on an unbroken series (a session indicator: counted from the first bar of the first session). For the pivots it is the
    /// earliest bar at which a pivot can be confirmed (<c>2n + 1</c>): they are ready from the first pivot on, which depends on the data. For a date-driven feature (<see cref="DateDriven"/>) it is counted in
    /// <b>days of the market context</b> and declared for the default <see cref="ContextOptions"/> (the instance a frame runs reports the number for the options of its own context): 1 for <c>day_of_week</c>,
    /// <c>is_expiry_day</c>, <c>vix</c> and <c>breadth</c>, 55 for <c>index_trend</c>, 265 for <c>regime</c> (see <see cref="DateFeature"/>).
    /// </summary>
    public int WarmUp(in FeatureSpec spec) => _warmUp(spec);

    /// <summary>
    /// A fresh instance for the spec (a <see cref="PriceSource.Default"/> source is resolved first). The spec must have been validated. A date-driven feature is created without a context or expiry dates, so only
    /// <c>day_of_week</c> is ever ready on it; the frame builds the others with <see cref="FrameOptions.Context"/> and <see cref="FrameOptions.ExpiryDays"/>.
    /// </summary>
    public IIndicator Create(in FeatureSpec spec) => _factory(Canonical(spec));

    /// <summary>A fresh instance for the spec, given the context and the expiry dates of the frame: a date-driven feature reads them, any other indicator is created as by <see cref="Create(in FeatureSpec)"/>.</summary>
    internal IIndicator CreateWith(in FeatureSpec spec, MarketContext? context, IReadOnlySet<DateOnly>? expiryDays) =>
        _dateFeature is { } kind ? new DateFeature(kind, context, expiryDays) : _factory(Canonical(spec));

    /// <summary>A spec of this indicator with the default parameters and source on the timeframe. The defaults are those of 1-minute bars: an opening range on a longer timeframe needs a whole number of bars, so its default may be refused there.</summary>
    public FeatureSpec DefaultSpec(Timeframe timeframe = Timeframe.M1) => new(
        Name,
        timeframe,
        DefaultSource,
        Parameters.Count > 0 ? Parameters[0].Default : 0,
        Parameters.Count > 1 ? Parameters[1].Default : 0,
        Parameters.Count > 2 ? Parameters[2].Default : 0,
        Parameters.Count > 3 ? Parameters[3].Default : 0);

    /// <summary>
    /// The same feature in its canonical form, so that two specs for one number are equal: <see cref="PriceSource.Default"/> becomes the indicator's <see cref="DefaultSource"/>, and a source that is ignored is
    /// <see cref="PriceSource.Close"/>.
    /// </summary>
    internal FeatureSpec Canonical(in FeatureSpec spec) =>
        !UsesSource ? spec with { Source = PriceSource.Close } : spec.Source == PriceSource.Default ? spec with { Source = DefaultSource } : spec;

    internal void Validate(in FeatureSpec spec, string path, FieldErrors errors)
    {
        var before = errors.Items.Count;
        if (!IndicatorCatalog.IsSupported(spec.Timeframe))
        {
            errors.Add(path + ".timeframe", "The timeframe must be one of 1m, 5m, 10m, 15m, 30m, 1h, 1D, 1W, 1M.");
        }
        else if (!Supports(spec.Timeframe))
        {
            errors.Add(path + ".timeframe", DateDriven
                ? Text($"'{Name}' changes once per date; it can only be asked for on 1m (the default), not on {spec.Timeframe.ToCode()}.")
                : IntradayOnly
                    ? Text($"'{Name}' needs intraday bars; it cannot be computed on {spec.Timeframe.ToCode()}.")
                    : Text($"'{Name}' is a value of the trading session; it cannot be computed on {spec.Timeframe.ToCode()} (read it on 1m or 1D)."));
        }

        if (spec.Source is not (PriceSource.Default or PriceSource.Close or PriceSource.Open or PriceSource.High or PriceSource.Low))
        {
            errors.Add(path + ".source", "The price source must be close, open, high or low (volume is the feature 'volume', not a source).");
        }

        for (var i = 0; i < 4; i++)
        {
            var value = spec.Parameter(i);
            if (i >= Parameters.Count)
            {
                if (value != 0)
                {
                    errors.Add(path + ".p" + (i + 1).ToString(CultureInfo.InvariantCulture), Text($"'{Name}' takes {Parameters.Count} parameter(s); the rest must be 0."));
                }

                continue;
            }

            var parameter = Parameters[i];
            var field = path + ".p" + (i + 1).ToString(CultureInfo.InvariantCulture);
            if (!double.IsFinite(value))
            {
                errors.Add(field, Text($"'{parameter.Name}' must be a finite number."));
            }
            else if (parameter.Kind == ParameterKind.Whole && value != Math.Floor(value))
            {
                errors.Add(field, Text($"'{parameter.Name}' must be a whole number."));
            }
            else if (value < parameter.Min || value > parameter.Max)
            {
                errors.Add(field, Text($"'{parameter.Name}' must be between {parameter.Min} and {parameter.Max}."));
            }
        }

        if (errors.Items.Count == before && _extraCheck?.Invoke(spec) is { } problem)
        {
            errors.Add(path + ".p" + (problem.Parameter + 1).ToString(CultureInfo.InvariantCulture), problem.Message);
        }
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
