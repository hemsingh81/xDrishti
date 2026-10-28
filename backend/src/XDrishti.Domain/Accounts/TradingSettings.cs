using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// Trading role of an account. The settings are kept when the role is switched off, but they are inactive then.
/// An enabled role is always complete: risk profile, capital, at least one style and instruments for every chosen style.
/// </summary>
public sealed class TradingSettings
{
    /// <summary>Largest capital accepted (keeps the value inside the column).</summary>
    public const decimal MaxCapitalAmount = 10_000_000_000m;

    public const int MaxCount = 100;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private TradingSettings(
        bool enabled,
        TradingMode mode,
        Guid? riskProfileId,
        CapitalSource capitalSource,
        decimal? capitalAmount,
        bool intraday,
        bool swing,
        Instruments intradayInstruments,
        Instruments swingBuyInstruments,
        Instruments swingSellInstruments,
        decimal? overrideRiskPerTradePct,
        int? overrideMaxNewEntriesPerDay,
        int? overrideMaxOpenPositions,
        decimal? overrideDailyLossPct)
    {
        Enabled = enabled;
        Mode = mode;
        RiskProfileId = riskProfileId;
        CapitalSource = capitalSource;
        CapitalAmount = capitalAmount;
        Intraday = intraday;
        Swing = swing;
        IntradayInstruments = intradayInstruments;
        SwingBuyInstruments = swingBuyInstruments;
        SwingSellInstruments = swingSellInstruments;
        OverrideRiskPerTradePct = overrideRiskPerTradePct;
        OverrideMaxNewEntriesPerDay = overrideMaxNewEntriesPerDay;
        OverrideMaxOpenPositions = overrideMaxOpenPositions;
        OverrideDailyLossPct = overrideDailyLossPct;
    }

    public bool Enabled { get; private set; }

    public TradingMode Mode { get; private set; }

    public Guid? RiskProfileId { get; private set; }

    public CapitalSource CapitalSource { get; private set; }

    public decimal? CapitalAmount { get; private set; }

    public bool Intraday { get; private set; }

    public bool Swing { get; private set; }

    public Instruments IntradayInstruments { get; private set; }

    public Instruments SwingBuyInstruments { get; private set; }

    public Instruments SwingSellInstruments { get; private set; }

    public decimal? OverrideRiskPerTradePct { get; private set; }

    public int? OverrideMaxNewEntriesPerDay { get; private set; }

    public int? OverrideMaxOpenPositions { get; private set; }

    public decimal? OverrideDailyLossPct { get; private set; }

    public TradingOverrides Overrides => new(OverrideRiskPerTradePct, OverrideMaxNewEntriesPerDay, OverrideMaxOpenPositions, OverrideDailyLossPct);

    /// <summary>The state of a new account: role off, paper mode, nothing chosen yet.</summary>
    internal static TradingSettings CreateDefault() =>
        new(false, TradingMode.Paper, null, CapitalSource.Fixed, null, false, false, Instruments.None, Instruments.None, Instruments.None, null, null, null, null);

    /// <summary>Replaces the configuration. Values must be in range always; completeness is required only while the role is on.</summary>
    internal Result Configure(TradingSpec spec)
    {
        var errors = Validate(spec, Enabled);
        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        Apply(spec);
        return Result.Success();
    }

    /// <summary>Whether a configuration is complete: profile, capital, at least one style and instruments for every chosen style (the test of an enabled role).</summary>
    public static bool IsComplete(TradingSpec spec) => !Validate(spec, requireComplete: true).HasErrors;

    /// <summary>Why the role cannot be switched on yet (field errors named like the API: <c>roles.trading</c>, <c>trading.riskProfileId</c>, …); empty when it can.</summary>
    internal FieldErrors CheckCanEnable()
    {
        var errors = Validate(ToSpec(), requireComplete: true);
        if (!errors.HasErrors)
        {
            return errors;
        }

        var wrapped = new FieldErrors();
        wrapped.Add("roles.trading", "Complete the trading settings before switching the role on.");
        wrapped.AddRange(errors, "trading");
        return wrapped;
    }

    /// <summary>Switches the role on or off (settings are kept). Call <see cref="CheckCanEnable"/> first when switching on.</summary>
    internal void SetEnabled(bool enabled) => Enabled = enabled;

    public TradingSpec ToSpec() =>
        new(Mode, RiskProfileId, CapitalSource, CapitalAmount, Intraday, Swing, IntradayInstruments, SwingBuyInstruments, SwingSellInstruments, Overrides);

    /// <summary>Checks a configuration; field names are relative to the trading object (for example <c>riskProfileId</c>).</summary>
    internal static FieldErrors Validate(TradingSpec spec, bool requireComplete)
    {
        var errors = new FieldErrors();
        if (!Enum.IsDefined(spec.Mode))
        {
            errors.Add("mode", "Unknown mode.");
        }

        if (!Enum.IsDefined(spec.CapitalSource))
        {
            errors.Add("capitalSource", "Unknown capital source.");
        }

        if (spec.CapitalAmount is { } amount)
        {
            Ranges.GreaterThanUpTo(errors, "capitalAmount", amount, 0m, MaxCapitalAmount);
        }

        CheckInstruments(errors, "instruments.intraday", spec.IntradayInstruments, Instruments.EquityIntraday | Instruments.Futures, "EQ_MIS and FUT");
        CheckInstruments(errors, "instruments.swingBuy", spec.SwingBuyInstruments, Instruments.EquityDelivery | Instruments.Futures, "EQ_CNC and FUT");
        CheckInstruments(errors, "instruments.swingSell", spec.SwingSellInstruments, Instruments.Futures, "FUT");
        CheckOverrides(errors, spec.Overrides);

        if (requireComplete)
        {
            CheckComplete(errors, spec);
        }

        return errors;
    }

    private static void CheckComplete(FieldErrors errors, TradingSpec spec)
    {
        if (spec.RiskProfileId is null)
        {
            errors.Add("riskProfileId", "A risk profile is required.");
        }

        if (spec.CapitalSource == CapitalSource.Fixed && spec.CapitalAmount is null)
        {
            errors.Add("capitalAmount", "Capital must be more than 0.");
        }

        if (!spec.Intraday && !spec.Swing)
        {
            errors.Add("styles", "Choose at least one style (intraday or swing).");
        }

        if (spec.Intraday && spec.IntradayInstruments == Instruments.None)
        {
            errors.Add("instruments.intraday", "Choose at least one instrument for intraday.");
        }

        if (spec.Swing && spec.SwingBuyInstruments == Instruments.None)
        {
            errors.Add("instruments.swingBuy", "Choose at least one instrument for swing buys.");
        }
    }

    private static void CheckInstruments(FieldErrors errors, string field, Instruments chosen, Instruments allowed, string allowedText)
    {
        if ((chosen & ~allowed) != Instruments.None)
        {
            errors.Add(field, $"Only {allowedText} are allowed here.");
        }
    }

    private static void CheckOverrides(FieldErrors errors, TradingOverrides overrides)
    {
        if (overrides.RiskPerTradePct is { } risk)
        {
            Ranges.GreaterThanUpTo(errors, "overrides.riskPerTradePct", risk, 0m, RiskLimits.MaxRiskPerTradePct);
        }

        if (overrides.DailyLossPct is { } loss)
        {
            Ranges.GreaterThanUpTo(errors, "overrides.dailyLossPct", loss, 0m, RiskLimits.MaxLossPct);
        }

        CheckCount(errors, "overrides.maxNewEntriesPerDay", overrides.MaxNewEntriesPerDay);
        CheckCount(errors, "overrides.maxOpenPositions", overrides.MaxOpenPositions);
    }

    private static void CheckCount(FieldErrors errors, string field, int? value)
    {
        if (value is < 1 or > MaxCount)
        {
            errors.Add(field, $"Must be between 1 and {MaxCount}.");
        }
    }

    private void Apply(TradingSpec spec)
    {
        Mode = spec.Mode;
        RiskProfileId = spec.RiskProfileId;
        CapitalSource = spec.CapitalSource;
        CapitalAmount = spec.CapitalAmount;
        Intraday = spec.Intraday;
        Swing = spec.Swing;
        IntradayInstruments = spec.IntradayInstruments;
        SwingBuyInstruments = spec.SwingBuyInstruments;
        SwingSellInstruments = spec.SwingSellInstruments;
        OverrideRiskPerTradePct = spec.Overrides.RiskPerTradePct;
        OverrideMaxNewEntriesPerDay = spec.Overrides.MaxNewEntriesPerDay;
        OverrideMaxOpenPositions = spec.Overrides.MaxOpenPositions;
        OverrideDailyLossPct = spec.Overrides.DailyLossPct;
    }
}
