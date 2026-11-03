using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// The rates in force from one date (ADR 0042, D4 and D7): the GST percent, the rounding rule and the rates of the two cash segments, and (P14-07, ADR 0194) the optional rates of the option and futures segments. A segment the owner gave no
/// rates for has none: <see langword="null"/>, never a zero line. A leg uses the schedule in force on its own date. Only <see cref="Create"/> builds one.
/// </summary>
public sealed record ChargeSchedule
{
    /// <summary>The first and last year a schedule may start in.</summary>
    internal const int FirstYear = 2000;

    internal const int LastYear = 2099;

    private ChargeSchedule(DateOnly effectiveFrom, decimal gstPct, ChargeRounding rounding, ChargeRates intradayEquity, ChargeRates delivery, ChargeRates? options, ChargeRates? futures)
    {
        EffectiveFrom = effectiveFrom;
        GstPct = gstPct;
        Rounding = rounding;
        IntradayEquity = intradayEquity;
        Delivery = delivery;
        Options = options;
        Futures = futures;
    }

    /// <summary>The first date the rates apply to (an India date).</summary>
    public DateOnly EffectiveFrom { get; }

    /// <summary>GST, percent of the brokerage, exchange and SEBI lines of the same leg.</summary>
    public decimal GstPct { get; }

    /// <summary>How each line is rounded to the paisa.</summary>
    public ChargeRounding Rounding { get; }

    /// <summary>The rates of an intraday cash-market trade.</summary>
    public ChargeRates IntradayEquity { get; }

    /// <summary>The rates of a delivery trade.</summary>
    public ChargeRates Delivery { get; }

    /// <summary>The rates of an option leg, when the owner gave them; null means the sheet has no option lines for these dates.</summary>
    public ChargeRates? Options { get; }

    /// <summary>The rates of a futures leg, when the owner gave them; null means the sheet has no futures lines for these dates.</summary>
    public ChargeRates? Futures { get; }

    /// <summary>The rates of the segment; null for an option or futures segment the schedule has no rates for (never a zero line).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The segment is not a member of <see cref="CostSegment"/>.</exception>
    public ChargeRates? TryRatesOf(CostSegment segment) => segment switch
    {
        CostSegment.IntradayEquity => IntradayEquity,
        CostSegment.Delivery => Delivery,
        CostSegment.Options => Options,
        CostSegment.Futures => Futures,
        _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, "Unknown cost segment."),
    };

    /// <summary>The rates of the segment.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The segment is not a member of <see cref="CostSegment"/>.</exception>
    /// <exception cref="InvalidOperationException">An option or futures segment the schedule has no rates for: ask <see cref="TryRatesOf"/>.</exception>
    public ChargeRates RatesOf(CostSegment segment) =>
        TryRatesOf(segment) ?? throw new InvalidOperationException($"The schedule from {EffectiveFrom:yyyy-MM-dd} has no rates for the {segment} segment.");

    /// <summary>Builds the schedule. A problem is a field error with the path <c>effective_from</c> or <c>gst_pct</c>.</summary>
    public static Result<ChargeSchedule> Create(DateOnly effectiveFrom, decimal gstPct, ChargeRounding rounding, ChargeRates intradayEquity, ChargeRates delivery, ChargeRates? options = null, ChargeRates? futures = null)
    {
        ArgumentNullException.ThrowIfNull(intradayEquity);
        ArgumentNullException.ThrowIfNull(delivery);
        var errors = new List<FieldError>();
        if (effectiveFrom.Year is < FirstYear or > LastYear)
        {
            errors.Add(new FieldError("effective_from", $"must be a date from {FirstYear} to {LastYear}.", null, CostErrors.OutOfRange));
        }

        CostField.Check(errors, "gst_pct", gstPct, CostField.MaxGstPercent);
        if (!Enum.IsDefined(rounding))
        {
            errors.Add(new FieldError("rounding", "is not a known rounding rule.", null, CostErrors.InvalidValue));
        }

        return errors.Count > 0
            ? Result.Failure<ChargeSchedule>(CostErrors.SheetInvalid(errors))
            : Result.Success(new ChargeSchedule(effectiveFrom, CostField.Normal(gstPct), rounding, intradayEquity, delivery, options, futures));
    }
}
