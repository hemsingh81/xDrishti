using System.Globalization;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Selection;

namespace XDrishti.Domain.Checklist;

/// <summary>
/// The pre-trade checklist (spec P7-05, ADR 0087): pure checks of one ticket for one paper-mode Trading account. An input that is missing, not finite or invalid
/// makes its check <see cref="FilterOutcome.Unavailable"/>, which is never a pass; only the owner's confirmation (<see cref="OwnerConfirmation"/>) lets such a ticket be approved, and the review then shows it as confirmed despite.
/// A live or non-Trading account is refused with one check and nothing else is evaluated.
/// </summary>
public static class PreTradeChecklist
{
    /// <summary>Checks the ticket for the account. Deterministic: same inputs, same checks in the same order.</summary>
    public static ChecklistResult Evaluate(ChecklistTicket ticket, ChecklistAccount account, ChecklistFacts facts, ChecklistLimits limits, ChecklistPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(policy);

        var paper = account.IsTrading && account.Mode == TradingMode.Paper;
        if (!paper)
        {
            var why = !account.IsTrading ? "the account does not have the Trading role" : "the account is not in paper mode";
            return new ChecklistResult(account.AccountId, ticket.Symbol, [new ChecklistCheck(ChecklistCodes.AccountPaper, FilterOutcome.Fail, CheckPolicy.BlockWithConfirm, null, null, "refused: " + why + "; only paper-mode Trading accounts receive tickets")]);
        }

        var checks = new List<ChecklistCheck>
        {
            new(ChecklistCodes.AccountPaper, FilterOutcome.Pass, CheckPolicy.BlockWithConfirm, null, null, "paper-mode Trading account"),
            RiskProfile(ticket, account, policy),
            Capital(ticket, account, policy),
            Exposure(ticket, account, policy),
            DailyLoss(account, policy),
            DataQuality(facts, policy),
            Holdings(ticket, facts, policy),
            Sector(ticket, account, policy),
            Results(facts, limits, policy),
            Ban(facts, policy),
            Surveillance(facts, policy),
            Band(ticket, facts, policy),
            Liquidity(ticket, facts, limits, policy),
            Correlation(facts, limits, policy),
        };
        return new ChecklistResult(account.AccountId, ticket.Symbol, checks);
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double? Pos(double? v) => v is { } x && double.IsFinite(x) && x > 0 ? x : null;

    private static double? NonNeg(double? v) => v is { } x && double.IsFinite(x) && x >= 0 ? x : null;

    private static ChecklistCheck Unavailable(string code, ChecklistPolicy p, string why) => new(code, FilterOutcome.Unavailable, p.Of(code), null, null, "unavailable: " + why);

    private static ChecklistCheck Judge(string code, ChecklistPolicy p, bool pass, double value, double limit, string detail) =>
        new(code, pass ? FilterOutcome.Pass : FilterOutcome.Fail, p.Of(code), value, limit, detail);

    private static ChecklistCheck RiskProfile(ChecklistTicket t, ChecklistAccount a, ChecklistPolicy p)
    {
        if (Pos(t.RiskPct) is not { } risk || Pos(a.MaxPerTradeRiskPct) is not { } max)
        {
            return Unavailable(ChecklistCodes.RiskProfile, p, "ticket risk or the account's per-trade limit is not known");
        }

        return Judge(ChecklistCodes.RiskProfile, p, Math.Round(risk, 9) <= max, risk, max, "risk of the trade " + N(risk) + "%, account limit " + N(max) + "%");
    }

    private static ChecklistCheck Capital(ChecklistTicket t, ChecklistAccount a, ChecklistPolicy p)
    {
        if (Pos(t.EntryPrice) is not { } price || Pos(t.Quantity) is not { } qty || NonNeg(a.FundsAvailable) is not { } funds)
        {
            return Unavailable(ChecklistCodes.CapitalHeadroom, p, "entry price, quantity or the account's funds are not known");
        }

        var cost = price * qty;
        return Judge(ChecklistCodes.CapitalHeadroom, p, Math.Round(cost, 9) <= funds, cost, funds, "cost " + N(cost) + ", funds available " + N(funds));
    }

    private static ChecklistCheck Exposure(ChecklistTicket t, ChecklistAccount a, ChecklistPolicy p)
    {
        if (Pos(t.RiskPct) is not { } risk || NonNeg(a.OpenRiskPct) is not { } open || Pos(a.MaxTotalOpenRiskPct) is not { } max)
        {
            return Unavailable(ChecklistCodes.ExposureHeadroom, p, "ticket risk, open risk or the account's total limit is not known");
        }

        var total = open + risk;
        return Judge(ChecklistCodes.ExposureHeadroom, p, Math.Round(total, 9) <= max, total, max, "total open risk would be " + N(total) + "%, limit " + N(max) + "%");
    }

    private static ChecklistCheck DailyLoss(ChecklistAccount a, ChecklistPolicy p)
    {
        if (NonNeg(a.DailyLossPct) is not { } loss || Pos(a.DailyLossLimitPct) is not { } max)
        {
            return Unavailable(ChecklistCodes.DailyLoss, p, "today's loss or the daily loss limit is not known");
        }

        return Judge(ChecklistCodes.DailyLoss, p, Math.Round(loss, 9) < max, loss, max, "loss today " + N(loss) + "%, daily limit " + N(max) + "% (reaching it stops new tickets)");
    }

    private static ChecklistCheck DataQuality(ChecklistFacts f, ChecklistPolicy p) => f.DataQualityOk switch
    {
        null => Unavailable(ChecklistCodes.DataQuality, p, "no validation status for the instrument"),
        true => new(ChecklistCodes.DataQuality, FilterOutcome.Pass, p.Of(ChecklistCodes.DataQuality), null, null, "latest validation is clean"),
        false => new(ChecklistCodes.DataQuality, FilterOutcome.Fail, p.Of(ChecklistCodes.DataQuality), null, null, "latest validation of the instrument failed"),
    };

    private static ChecklistCheck Holdings(ChecklistTicket t, ChecklistFacts f, ChecklistPolicy p)
    {
        var code = ChecklistCodes.HoldingsConflict;
        var sell = t.Side == "SELL";
        if (t.Side is not ("BUY" or "SELL"))
        {
            return Unavailable(code, p, "the side is not BUY or SELL");
        }

        if (f.OpenTradeExists == true)
        {
            return new(code, FilterOutcome.Fail, p.Of(code), null, null, "a trade in the instrument is already open");
        }

        if (sell && f.HeldLongAnyAccount == true)
        {
            return new(code, FilterOutcome.Fail, p.Of(code), null, null, "SELL on an instrument held long");
        }

        if (f.OpenTradeExists is null || (sell && f.HeldLongAnyAccount is null))
        {
            return Unavailable(code, p, "open trades or holdings are not known");
        }

        return new(code, FilterOutcome.Pass, p.Of(code), null, null, "no conflict with holdings or open trades");
    }

    private static ChecklistCheck Sector(ChecklistTicket t, ChecklistAccount a, ChecklistPolicy p)
    {
        if (string.IsNullOrWhiteSpace(t.Sector) || a.SectorPositions is not { } n || n < 0 || a.MaxPerSector is not { } max || max < 1)
        {
            return Unavailable(ChecklistCodes.SectorExposure, p, "sector, the count in it or the account's sector limit is not known");
        }

        var after = n + 1;
        return Judge(ChecklistCodes.SectorExposure, p, after <= max, after, max, after + " in the sector with this ticket, limit " + max);
    }

    private static ChecklistCheck Results(ChecklistFacts f, ChecklistLimits l, ChecklistPolicy p)
    {
        if (f.DaysToResults is not { } d || d < 0)
        {
            return Unavailable(ChecklistCodes.ResultsWithinDays, p, "no results date for the instrument (no data source yet)");
        }

        return Judge(ChecklistCodes.ResultsWithinDays, p, d > l.ResultsWithinDays, d, l.ResultsWithinDays, "results in " + d + " days, flagged within " + l.ResultsWithinDays);
    }

    private static ChecklistCheck Ban(ChecklistFacts f, ChecklistPolicy p) => f.InFnoBan switch
    {
        null => Unavailable(ChecklistCodes.FnoBan, p, "no F&O ban list (no data source yet)"),
        true => new(ChecklistCodes.FnoBan, FilterOutcome.Fail, p.Of(ChecklistCodes.FnoBan), null, null, "the instrument is in the F&O ban list"),
        false => new(ChecklistCodes.FnoBan, FilterOutcome.Pass, p.Of(ChecklistCodes.FnoBan), null, null, "not in the F&O ban list"),
    };

    private static ChecklistCheck Surveillance(ChecklistFacts f, ChecklistPolicy p) => f.UnderSurveillance switch
    {
        null => Unavailable(ChecklistCodes.Surveillance, p, "surveillance status is not known"),
        true => new(ChecklistCodes.Surveillance, FilterOutcome.Fail, p.Of(ChecklistCodes.Surveillance), null, null, "the instrument is under exchange surveillance"),
        false => new(ChecklistCodes.Surveillance, FilterOutcome.Pass, p.Of(ChecklistCodes.Surveillance), null, null, "not under surveillance"),
    };

    private static ChecklistCheck Band(ChecklistTicket t, ChecklistFacts f, ChecklistPolicy p)
    {
        if (Pos(t.EntryPrice) is not { } price || Pos(f.PriceBandLower) is not { } lo || Pos(f.PriceBandUpper) is not { } hi || lo >= hi)
        {
            return Unavailable(ChecklistCodes.PriceBand, p, "entry price or the price band is not known");
        }

        // At a band edge the price is circuit-locked and cannot be traded, so only strictly inside passes.
        return Judge(ChecklistCodes.PriceBand, p, price > lo && price < hi, price, price <= lo ? lo : hi, "entry " + N(price) + ", band " + N(lo) + " to " + N(hi) + " (an edge is a locked circuit)");
    }

    private static ChecklistCheck Liquidity(ChecklistTicket t, ChecklistFacts f, ChecklistLimits l, ChecklistPolicy p)
    {
        if (Pos(t.Quantity) is not { } qty || Pos(f.AverageDailyVolume) is not { } adv)
        {
            return Unavailable(ChecklistCodes.Liquidity, p, "quantity or average daily volume is not known");
        }

        var pct = qty / adv * 100;
        return Judge(ChecklistCodes.Liquidity, p, Math.Round(pct, 9) <= l.MaxPctOfAverageDailyVolume, pct, l.MaxPctOfAverageDailyVolume, "quantity is " + N(pct) + "% of the average daily volume, limit " + N(l.MaxPctOfAverageDailyVolume) + "%");
    }

    private static ChecklistCheck Correlation(ChecklistFacts f, ChecklistLimits l, ChecklistPolicy p)
    {
        if (f.MaxCorrelation is not { } c || !double.IsFinite(c) || c is < -1 or > 1)
        {
            return Unavailable(ChecklistCodes.Correlation, p, "correlation with the book is not computed yet (P7-06)");
        }

        return Judge(ChecklistCodes.Correlation, p, Math.Round(c, 9) <= l.MaxCorrelation, c, l.MaxCorrelation, "highest correlation " + N(c) + ", limit " + N(l.MaxCorrelation));
    }
}
