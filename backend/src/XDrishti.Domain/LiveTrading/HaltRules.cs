using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.LiveTrading;

/// <summary>The words of a halt (spec P11-04, table <c>trading.live_halts</c>).</summary>
public static class HaltCodes
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Opened = "opened";
    public const string Closed = "closed";
    public const string StopNew = "stop_new";
    public const string Halve = "halve";
    public const string AlertOnly = "alert_only";
}

/// <summary>A limit reached: what the loss was against the limit, and what the halt does.</summary>
public sealed record HaltDecision(string Kind, double LossR, double LimitR, double? LossPct, double LimitPct, string Action, string MeasureState);

/// <summary>
/// The daily and weekly halts (spec P11-04, ADR 0151), pure. The unit is the current scaled 1 R of the account (its capital times the live risk percent). A halt opens when the loss reaches the R limit or the percentage limit, whichever comes
/// first; at S1 the R limit binds long before the percentage one, which the screen says. A measure that is not a value never opens a halt and never reads as "no loss": it is shown as unavailable and fails the live-ready gate.
/// </summary>
public static class HaltRules
{
    /// <summary>The scaled 1 R in rupees: capital times (profile risk percent times the scale) over 100; null without a positive capital, risk or scale.</summary>
    public static decimal? OneR(decimal? capital, double? liveRiskPct) =>
        capital is > 0 && liveRiskPct is { } p && double.IsFinite(p) && p > 0 ? Math.Round(capital.Value * (decimal)p / 100m, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>The daily decision for a day's loss; null when the loss is below both limits or not measurable (value and realised_only both count: an unmarked open lot can only make the true loss larger).</summary>
    public static HaltDecision? Daily(DayLoss day, decimal? oneR, decimal? capital, decimal limitR, decimal limitPct) => Decide(HaltCodes.Daily, day.State, day.Loss, oneR, capital, limitR, limitPct, HaltCodes.StopNew);

    /// <summary>The weekly decision for the week's loss (the caller sums each day's net, gains offsetting losses across the week, and floors the result at 0); the action is the profile's.</summary>
    public static HaltDecision? Weekly(string state, decimal? weekLoss, decimal? oneR, decimal? capital, decimal limitR, WeeklyLossAction action) =>
        Decide(HaltCodes.Weekly, state, weekLoss, oneR, capital, limitR, decimal.MaxValue, Action(action));

    public static string Action(WeeklyLossAction action) => action switch
    {
        WeeklyLossAction.HalveSize => HaltCodes.Halve,
        WeeklyLossAction.StopNewEntries => HaltCodes.StopNew,
        _ => HaltCodes.AlertOnly,
    };

    private static HaltDecision? Decide(string kind, string state, decimal? loss, decimal? oneR, decimal? capital, decimal limitR, decimal limitPct, string action)
    {
        if (state == MeasureStates.Unavailable || loss is not { } l || oneR is not { } r || r <= 0)
        {
            return null;
        }

        var lossR = (double)(l / r);
        double? lossPct = capital is > 0 ? (double)(l / capital.Value * 100m) : null;
        var hit = lossR >= (double)limitR || (limitPct != decimal.MaxValue && lossPct is { } p && p >= (double)limitPct);
        return hit ? new HaltDecision(kind, lossR, (double)limitR, lossPct, limitPct == decimal.MaxValue ? 0 : (double)limitPct, action, state) : null;
    }
}
