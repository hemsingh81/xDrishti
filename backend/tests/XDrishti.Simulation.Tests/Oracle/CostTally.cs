using System.Globalization;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// What a cost run over one source found, summed: the trades costed, the fates, the oracle's trace counted per trade (so that a coverage floor is measured from the oracle's trace and not from the engine), the sub-cases of the spec that no
/// trace id names (equal to the limit, a delivery sell with the DP charge, a swing trade across a schedule change), the capitals seen, and every disagreement with its key.
/// </summary>
internal sealed class CostTally(string name)
{
    internal const string EqualToLimit = "equal-to-limit";

    internal const string DpOnDeliverySell = "dp-on-delivery-sell";

    internal const string SwingAcrossScheduleChange = "swing-across-schedule-change";

    internal const string JustOverLimit = "just-over-limit";

    internal const string LimitClamped = "limit-clamped-to-the-engine-bound";

    private const int KeptDisagreements = 400;

    internal string Name { get; } = name;

    /// <summary>The trades costed (a trade of the chain is costed on several sheets, so <see cref="Cases"/> is larger).</summary>
    internal int Trades { get; set; }

    internal int Cases { get; private set; }

    internal int Kept { get; private set; }

    internal int SkippedQuantity { get; private set; }

    internal int SkippedCost { get; private set; }

    internal int Errors { get; private set; }

    internal decimal MinCapital { get; private set; } = decimal.MaxValue;

    internal decimal MaxCapital { get; private set; }

    /// <summary>Cases whose oracle trace contains a rule-row id, and the named sub-cases.</summary>
    internal Dictionary<string, int> Rows { get; } = new(StringComparer.Ordinal);

    /// <summary>Kept trades checked against the chain <c>RNet &lt;= RFilled &lt;= RExact</c> with <c>RNet = RFilled</c> only when the total is 0 (the oracle's costing and the engine's, each counted), and the ones that broke it.</summary>
    internal int ChainChecked { get; private set; }

    internal int ChainBroken { get; private set; }

    internal int DisagreementCount { get; private set; }

    internal List<string> Disagreements { get; } = [];

    internal void Add(CostOutcome outcome, string where)
    {
        Cases++;
        switch (outcome.EngineView.Fate)
        {
            case CostView.Kept:
                Kept++;
                break;
            case CostView.SkipQuantity:
                SkippedQuantity++;
                break;
            case CostView.SkipCost:
                SkippedCost++;
                break;
            default:
                Errors++;
                break;
        }

        var input = outcome.Input;
        if (outcome.EngineView.Fate == CostView.Kept)
        {
            foreach (var view in new[] { outcome.OracleView, outcome.EngineView })
            {
                ChainChecked++;
                if (!ChainHolds(input, view))
                {
                    ChainBroken++;
                }
            }
        }

        if (CostAdapter.Clamped(input.MaxCostR))
        {
            Count(LimitClamped);
        }

        MinCapital = Math.Min(MinCapital, input.Capital);
        MaxCapital = Math.Max(MaxCapital, input.Capital);
        if (outcome.Oracle is { } r)
        {
            foreach (var id in r.Trace)
            {
                Count(id);
            }

            if (r.Skip == OracleCostSkip.None && r.CostR == input.MaxCostR)
            {
                Count(EqualToLimit);
            }

            if (r.Skip == OracleCostSkip.Cost && r.CostR > input.MaxCostR)
            {
                Count(JustOverLimit);
            }

            var sell = (input.Side == OracleSide.Sell ? r.EntryLeg : r.ExitLeg)?.Dp > 0m;
            if (input.Segment == OracleCostSegment.Delivery && sell)
            {
                Count(DpOnDeliverySell);
            }

            if (input.Segment == OracleCostSegment.Delivery && input.ExitDate > input.EntryDate && r.Trace.Contains("C-DATE-CHANGE"))
            {
                Count(SwingAcrossScheduleChange);
            }
        }

        foreach (var mismatch in outcome.Mismatches)
        {
            DisagreementCount++;
            if (Disagreements.Count < KeptDisagreements)
            {
                Disagreements.Add(string.Create(CultureInfo.InvariantCulture, $"{Name} {where}: {mismatch}"));
            }
        }
    }

    /// <summary>Adds the counts of another tally of the same source (the runs are made in parallel and folded in a fixed order).</summary>
    internal void Merge(CostTally other)
    {
        Trades += other.Trades;
        Cases += other.Cases;
        Kept += other.Kept;
        SkippedQuantity += other.SkippedQuantity;
        SkippedCost += other.SkippedCost;
        Errors += other.Errors;
        ChainChecked += other.ChainChecked;
        ChainBroken += other.ChainBroken;
        MinCapital = Math.Min(MinCapital, other.MinCapital);
        MaxCapital = Math.Max(MaxCapital, other.MaxCapital);
        foreach (var (key, value) in other.Rows)
        {
            Rows[key] = Rows.GetValueOrDefault(key) + value;
        }

        DisagreementCount += other.DisagreementCount;
        foreach (var line in other.Disagreements.Where(_ => Disagreements.Count < KeptDisagreements))
        {
            Disagreements.Add(line);
        }
    }

    /// <summary>The chain of ADR 0042 D13 for one costed view: net R is not above filled R, filled R is not above exact R (up to the last digit of the last place the walker rounds to), and net R equals filled R exactly when the total charge is 0.</summary>
    internal static bool ChainHolds(OracleCostInput input, CostView view) =>
        view.RNet <= input.RFilled
        && input.RFilled <= input.RExact + RatioStats.Tolerance
        && ((view.TotalCost == 0m) == (view.RNet == input.RFilled));

    internal int Row(string id) => Rows.GetValueOrDefault(id);

    /// <summary>The summary of the run as text, in a fixed order: the same seeds give the same bytes.</summary>
    internal string Summary()
    {
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{Name}: trades {Trades}, costings {Cases}, kept {Kept}, skipped by quantity {SkippedQuantity}, skipped by cost {SkippedCost}, errors {Errors}, chain checked {ChainChecked} broken {ChainBroken}, capital {MinCapital} to {MaxCapital}, disagreements {DisagreementCount}"),
            "rows: " + string.Join(",", Rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Key + "=" + r.Value.ToString(CultureInfo.InvariantCulture))),
        };
        lines.AddRange(Disagreements.Order(StringComparer.Ordinal));
        return string.Join("\n", lines);
    }

    private void Count(string id) => Rows[id] = Rows.GetValueOrDefault(id) + 1;
}
