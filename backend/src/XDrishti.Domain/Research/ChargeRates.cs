using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// Every charge line of one segment as an explicit field (ADR 0042, D4): a zero is written 0, never null, so a missing rate cannot reach the model. Percent fields are in percent (0.03 means 0.03 %). The brokerage rule is closed:
/// a cap of 0 means brokerage is 0 (the percent and the minimum must be 0 too), never "no cap"; with a cap above 0 the minimum is at most the cap; a flat fee per order is a percent of 0 with the minimum equal to the cap. Only
/// <see cref="Create"/> builds one. The numbers are the sheet's; a rate that the repository does not hold is never invented in <c>src/</c>.
/// </summary>
public sealed record ChargeRates
{
    private ChargeRates(
        decimal brokeragePct, decimal brokerageCap, decimal brokerageMin, decimal sttBuyPct, decimal sttSellPct, decimal exchangeTxnPct, decimal sebiPerCrore, decimal stampBuyPct, decimal otherPct, decimal dpChargePerSell)
    {
        BrokeragePct = brokeragePct;
        BrokerageCap = brokerageCap;
        BrokerageMin = brokerageMin;
        SttBuyPct = sttBuyPct;
        SttSellPct = sttSellPct;
        ExchangeTxnPct = exchangeTxnPct;
        SebiPerCrore = sebiPerCrore;
        StampBuyPct = stampBuyPct;
        OtherPct = otherPct;
        DpChargePerSell = dpChargePerSell;
    }

    /// <summary>The names of the ten fields in the canonical order (the JSON keys).</summary>
    internal static IReadOnlyList<string> FieldNames { get; } = ["brokerage_pct", "brokerage_cap", "brokerage_min", "stt_buy_pct", "stt_sell_pct", "exchange_txn_pct", "sebi_per_crore", "stamp_buy_pct", "other_pct", "dp_charge_per_sell"];

    /// <summary>Brokerage as a percent of the turnover of one order.</summary>
    public decimal BrokeragePct { get; }

    /// <summary>The most brokerage of one order, in rupees; 0 means brokerage is 0.</summary>
    public decimal BrokerageCap { get; }

    /// <summary>The least brokerage of one order, in rupees; 0 means none.</summary>
    public decimal BrokerageMin { get; }

    /// <summary>Securities transaction tax on a buy order, percent of turnover.</summary>
    public decimal SttBuyPct { get; }

    /// <summary>Securities transaction tax on a sell order, percent of turnover.</summary>
    public decimal SttSellPct { get; }

    /// <summary>Exchange transaction charge, percent of turnover, both sides.</summary>
    public decimal ExchangeTxnPct { get; }

    /// <summary>SEBI fee, rupees per 1,00,00,000 of turnover, both sides.</summary>
    public decimal SebiPerCrore { get; }

    /// <summary>Stamp duty on a buy order, percent of turnover.</summary>
    public decimal StampBuyPct { get; }

    /// <summary>The "other" column of a bill, percent of turnover, both sides; never negative.</summary>
    public decimal OtherPct { get; }

    /// <summary>The depository charge of one delivery sell order, in rupees.</summary>
    public decimal DpChargePerSell { get; }

    /// <summary>
    /// Builds the rates. A problem is a field error whose path is the JSON key of the field (<c>stt_sell_pct</c>) and whose code is one of <see cref="CostErrors"/>; all problems are listed.
    /// </summary>
    public static Result<ChargeRates> Create(
        decimal brokeragePct, decimal brokerageCap, decimal brokerageMin, decimal sttBuyPct, decimal sttSellPct, decimal exchangeTxnPct, decimal sebiPerCrore, decimal stampBuyPct, decimal otherPct, decimal dpChargePerSell)
    {
        var errors = new List<FieldError>();
        var names = FieldNames;
        var ok = true;
        ok &= CostField.Check(errors, names[0], brokeragePct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[1], brokerageCap, CostField.MaxBrokerageRupees);
        ok &= CostField.Check(errors, names[2], brokerageMin, CostField.MaxBrokerageRupees);
        ok &= CostField.Check(errors, names[3], sttBuyPct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[4], sttSellPct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[5], exchangeTxnPct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[6], sebiPerCrore, CostField.MaxSebiPerCrore);
        ok &= CostField.Check(errors, names[7], stampBuyPct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[8], otherPct, CostField.MaxPercent);
        ok &= CostField.Check(errors, names[9], dpChargePerSell, CostField.MaxDpCharge);
        if (ok)
        {
            if (brokerageCap == 0 && (brokeragePct > 0 || brokerageMin > 0))
            {
                errors.Add(new FieldError(names[1], "a brokerage cap of 0 means no brokerage: the brokerage share of turnover and the minimum must be 0 too (a cap is never optional).", null, CostErrors.BrokerageCapRequired));
            }
            else if (brokerageCap > 0 && brokerageMin > brokerageCap)
            {
                errors.Add(new FieldError(names[2], "the brokerage minimum must not be above the cap.", null, CostErrors.MinAboveCap));
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure<ChargeRates>(CostErrors.SheetInvalid(errors));
        }

        return Result.Success(new ChargeRates(
            CostField.Normal(brokeragePct), CostField.Normal(brokerageCap), CostField.Normal(brokerageMin), CostField.Normal(sttBuyPct), CostField.Normal(sttSellPct),
            CostField.Normal(exchangeTxnPct), CostField.Normal(sebiPerCrore), CostField.Normal(stampBuyPct), CostField.Normal(otherPct), CostField.Normal(dpChargePerSell)));
    }

    /// <summary>The ten values in the canonical order.</summary>
    internal decimal[] Values() => [BrokeragePct, BrokerageCap, BrokerageMin, SttBuyPct, SttSellPct, ExchangeTxnPct, SebiPerCrore, StampBuyPct, OtherPct, DpChargePerSell];
}
