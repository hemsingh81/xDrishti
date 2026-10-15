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

    private readonly Func<FeatureSpec, string?>? _extraCheck;

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
        Func<FeatureSpec, string?>? extraCheck = null)
    {
        Name = name;
        Summary = summary;
        Parameters = parameters;
        SessionBehaviour = sessionBehaviour;
        UsesSource = usesSource;
        DefaultSource = defaultSource;
        IntradayOnly = intradayOnly;
        _warmUp = warmUp;
        _factory = factory;
        _extraCheck = extraCheck;
    }

    /// <summary>The lower-case snake_case name used in specs and in the DSL.</summary>
    public string Name { get; }

    public string Summary { get; }

    /// <summary>The parameters in the order of <see cref="FeatureSpec.P1"/> … <see cref="FeatureSpec.P4"/> (at most four).</summary>
    public IReadOnlyList<IndicatorParameter> Parameters { get; }

    public SessionBehaviour SessionBehaviour { get; }

    /// <summary>Whether the indicator reads a chosen price (<see cref="FeatureSpec.Source"/>); the others read the whole bar and treat all sources as the same feature.</summary>
    public bool UsesSource { get; }

    public PriceSource DefaultSource { get; }

    /// <summary>Whether the indicator only makes sense on intraday bars (it is refused on 1D).</summary>
    public bool IntradayOnly { get; }

    /// <summary>The bars of the spec's own timeframe after which the indicator is first ready on an unbroken series (a session indicator: counted from the first bar of the first session).</summary>
    public int WarmUp(in FeatureSpec spec) => _warmUp(spec);

    /// <summary>A fresh instance for the spec. The spec must have been validated.</summary>
    public IIndicator Create(in FeatureSpec spec) => _factory(spec);

    /// <summary>A spec of this indicator with the default parameters and source on the timeframe. The defaults are those of 1-minute bars: an opening range on a longer timeframe needs a whole number of bars, so its default may be refused there.</summary>
    public FeatureSpec DefaultSpec(Timeframe timeframe = Timeframe.M1) => new(
        Name,
        timeframe,
        DefaultSource,
        Parameters.Count > 0 ? Parameters[0].Default : 0,
        Parameters.Count > 1 ? Parameters[1].Default : 0,
        Parameters.Count > 2 ? Parameters[2].Default : 0,
        Parameters.Count > 3 ? Parameters[3].Default : 0);

    /// <summary>The same feature in its canonical form (a source that is ignored is <see cref="PriceSource.Close"/>), so that two specs for one number are equal.</summary>
    internal FeatureSpec Canonical(in FeatureSpec spec) => UsesSource ? spec : spec with { Source = PriceSource.Close };

    internal void Validate(in FeatureSpec spec, string path, FieldErrors errors)
    {
        var before = errors.Items.Count;
        if (!IndicatorCatalog.IsSupported(spec.Timeframe))
        {
            errors.Add(path + ".timeframe", "The timeframe must be one of 1m, 5m, 10m, 15m, 30m, 1h, 1D.");
        }
        else if (IntradayOnly && spec.Timeframe == Timeframe.D1)
        {
            errors.Add(path + ".timeframe", Text($"'{Name}' needs intraday bars; it cannot be computed on 1D."));
        }

        if (!Enum.IsDefined(spec.Source))
        {
            errors.Add(path + ".source", "The price source must be close, open, high or low.");
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
            errors.Add(path + ".p1", problem);
        }
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
