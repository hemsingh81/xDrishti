using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// Pure rules that describe why a closed trade lost (P6-15, ADR 0071, doc 09 table row 9). Reads only the fields of the closed <see cref="SimTrade"/> (prices, excursions, slippage, timestamps up to the exit), no later bar and no other trade, so the
/// tags are a function of the trade alone. They are descriptions of the past and must never become decision-time features. A loss that matches no rule is untagged (an empty list), which is an ordinary outcome.
/// </summary>
public static class LossTagger
{
    /// <summary>The version of the rules; changing any rule or default raises it.</summary>
    public const string RuleVersion = "lt-2";

    /// <summary>The tags of a trade, in the order of <see cref="LossTagKind"/>; the caller decides what a loss is (R below 0). A stop-out that is a loss only after costs (exact R not below 0) gets no NormalVariance and is untagged unless another rule fits.</summary>
    public static IReadOnlyList<LossTag> Tag(SimTrade trade, LossTagOptions options)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(options);
        var tags = new List<LossTag>();
        var c = CultureInfo.InvariantCulture;
        var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
        var stopped = trade.ExitReason == ExitReason.Stop;
        // The gap rule measures against the initial StopPrice: a stop trailed above it that fills at the trailed level is not a gap, so it gets no tag.

        if (stopped && risk > 0)
        {
            var beyond = trade.Side == Side.Buy ? trade.StopPrice - trade.ExitPrice : trade.ExitPrice - trade.StopPrice;
            var gapR = beyond / risk;
            if (gapR >= options.GapMinR)
            {
                tags.Add(new LossTag(LossTagKind.GapThroughStop, gapR, options.GapMinR, string.Create(c, $"stop exit {gapR:F2} R beyond the stop level (exit {trade.ExitPrice:F2}, stop {trade.StopPrice:F2}); threshold {options.GapMinR:F2} R")));
            }
        }

        if (trade.MfeR >= options.GaveBackMfeR)
        {
            tags.Add(new LossTag(LossTagKind.GaveBackProfit, trade.MfeR, options.GaveBackMfeR, string.Create(c, $"reached +{trade.MfeR:F2} R, ended {trade.RExact:F2} R; threshold +{options.GaveBackMfeR:F2} R")));
        }

        if (trade.Fills == FillBasis.Modelled && -trade.SlippageR >= options.HighSlippageR)
        {
            tags.Add(new LossTag(LossTagKind.HighSlippage, -trade.SlippageR, options.HighSlippageR, string.Create(c, $"fills cost {-trade.SlippageR:F2} R against the exact prices; threshold {options.HighSlippageR:F2} R")));
        }

        // Decision-minute convention of SimTrade.Key and EntryWindowLearner: the minute that closed the evaluation bar. A fill on a later date than the decision (an order that rested overnight) is not a late entry.
        var decisionInstant = trade.DecisionEpochSeconds - 60;
        var decisionMinute = SessionClock.MinuteOfDay(decisionInstant);
        var sameDate = SessionClock.DateOf(decisionInstant) == SessionClock.DateOf(trade.EntryEpochSeconds);
        if (trade.SessionsHeld == 1 && sameDate && decisionMinute >= options.LateAfterMinute)
        {
            tags.Add(new LossTag(LossTagKind.LateEntry, decisionMinute, options.LateAfterMinute, string.Create(c, $"bar closed at {Clock(decisionMinute)}, a trade that left on its entry date; late from {Clock(options.LateAfterMinute)}")));
        }

        var heldMinutes = (trade.ExitEpochSeconds - trade.EntryEpochSeconds) / 60.0;
        if (stopped && heldMinutes <= options.QuickStopMinutes && trade.MfeR < options.QuickStopMaxMfeR)
        {
            tags.Add(new LossTag(LossTagKind.QuickStop, heldMinutes, options.QuickStopMinutes, string.Create(c, $"stopped {heldMinutes:F0} min after the fill with best +{trade.MfeR:F2} R; threshold {options.QuickStopMinutes} min and +{options.QuickStopMaxMfeR:F2} R")));
        }

        if (trade.EntryPrice > 0 && risk / trade.EntryPrice >= options.WideStopFraction)
        {
            var fraction = risk / trade.EntryPrice;
            tags.Add(new LossTag(LossTagKind.WideStop, fraction, options.WideStopFraction, string.Create(c, $"stop {fraction:P2} of the entry price away; threshold {options.WideStopFraction:P2}")));
        }

        if (tags.Count == 0 && stopped && trade.RExact < 0 && trade.RExact >= -options.NormalVarianceMaxLossR)
        {
            tags.Add(new LossTag(LossTagKind.NormalVariance, trade.RExact, -options.NormalVarianceMaxLossR, string.Create(c, $"stopped out at {trade.RExact:F2} R with none of the other signatures; the ordinary cost of the strategy")));
        }

        return tags;
    }

    private static string Clock(int minute) => string.Create(CultureInfo.InvariantCulture, $"{minute / 60:D2}:{minute % 60:D2}");
}
