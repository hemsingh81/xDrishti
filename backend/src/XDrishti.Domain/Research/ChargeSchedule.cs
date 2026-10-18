using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// The rates in force from one date (ADR 0042, D4 and D7): the GST percent, the rounding rule and the rates of the two segments. A leg uses the schedule in force on its own date. Only <see cref="Create"/> builds one.
/// </summary>
public sealed record ChargeSchedule
{
    /// <summary>The first and last year a schedule may start in.</summary>
    internal const int FirstYear = 2000;

    internal const int LastYear = 2099;

    private ChargeSchedule(DateOnly effectiveFrom, decimal gstPct, ChargeRounding rounding, ChargeRates intradayEquity, ChargeRates delivery)
    {
        EffectiveFrom = effectiveFrom;
        GstPct = gstPct;
        Rounding = rounding;
        IntradayEquity = intradayEquity;
        Delivery = delivery;
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

    /// <summary>The rates of the segment.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The segment is not a member of <see cref="CostSegment"/>.</exception>
    public ChargeRates RatesOf(CostSegment segment) => segment switch
    {
        CostSegment.IntradayEquity => IntradayEquity,
        CostSegment.Delivery => Delivery,
        _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, "Unknown cost segment."),
    };

    /// <summary>Builds the schedule. A problem is a field error with the path <c>effective_from</c> or <c>gst_pct</c>.</summary>
    public static Result<ChargeSchedule> Create(DateOnly effectiveFrom, decimal gstPct, ChargeRounding rounding, ChargeRates intradayEquity, ChargeRates delivery)
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
            : Result.Success(new ChargeSchedule(effectiveFrom, CostField.Normal(gstPct), rounding, intradayEquity, delivery));
    }
}
