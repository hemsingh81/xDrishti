using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// The labels that say what a P3 number means (spec P3-04b1, E5; the roadmap section "What a P3 number means"): printed first on the console, written into <c>run.json</c> and <c>README.txt</c>, shown above every trade card. A label is
/// built from the run record and the fill rules it ran with, and the rules must be the ones the run recorded, so that a label cannot say a thing the run did not do. The text names no figure about performance.
/// </summary>
public static class RunLabels
{
    /// <summary>The engine version the oracle comparison (P3-08, P3-08c, ADR 0047) was shown for. The test project that holds the comparison keeps <c>OracleVerifiedFor</c> and fails unless it equals this and the engine's version, so bumping the engine without rerunning the comparison fails the check.</summary>
    public const string VerifiedFor = "sim-9";

    /// <summary>
    /// The engine line for a run of <see cref="VerifiedFor"/>: a separately written reference implementation of the walk, the position gate and the costing, written from the rule tables by a second agent (a process that is checked and not
    /// enforced), agrees with the engine. It says nothing about the tables being right (charts, P3-09) or about the owner's charge sheet.
    /// </summary>
    public const string EngineVerification = "engine: agrees with a separately written reference implementation of the rule tables (P3-08, " + VerifiedFor + "); not checked against charts or the owner's charge sheet";

    /// <summary>The engine line of a run: the agreement wording only when the run's engine version is the one the comparison was shown for; any other version says it has not been verified.</summary>
    public static string EngineLine(string engineVersion) => IsVerified(engineVersion) ? EngineVerification : "engine: not yet verified for " + engineVersion;

    /// <summary>Whether a run of this engine version may carry the agreement label; <c>run.json</c> writes it as <c>engine_verified</c>.</summary>
    public static bool IsVerified(string engineVersion) => string.Equals(engineVersion, VerifiedFor, StringComparison.Ordinal);

    /// <summary>The first label: a run is an exploration.</summary>
    public const string Exploration = "exploration: signal-level mechanics on past data, not evidence of an edge";

    /// <summary>The fills label when the slippage is none: <see cref="FillBasis.Exact"/> means no slippage and no tick rounding of a fill, and "fills modelled" is never dropped.</summary>
    public const string FillsWithoutSlippage = "fills: modelled, no slippage, no tick rounding of fills";

    private const string Fills = "fills: modelled";

    /// <summary>The label of a Lab run posted with conservative slippage (CF-05, ADR 0229): what the option does and what it leaves out.</summary>
    public const string ConservativeSlippage = "slippage: conservative (tier C and unavailable excluded)";

    /// <summary>The label of a Lab run posted with the cost stress explicitly off (CF-05, ADR 0229).</summary>
    public const string StressOff = "stress: off";

    /// <summary>The label of a robust Lab run posted with the chance and plateau block left out (<c>nulls: false</c>, CF-07, ADR 0231).</summary>
    public const string NullsOff = "nulls: off (no random-entry null, placebo set, mirror or plateau)";

    /// <summary>The label of a robust Lab run posted with its own number of random-entry seeds (CF-07, ADR 0231): a what-if, never scorecard evidence.</summary>
    public static string Draws(int draws) => "draws: " + draws.ToString(System.Globalization.CultureInfo.InvariantCulture) + " random-entry seeds (not the configured number)";

    private const string SurvivorshipNote = "; survivorship is bounded and disclosed, not avoided (ISS-22)";

    private const string TickLabel = "tick: today's master tick; rounding to the grid is approximate";

    private const string OpeningBarLabel = "opening bar: the stored 09:15 bar can be widened by the CSV importer and the first 1-minute open is not the auction open (ISS-21, ISS-18)";

    private const double DaysPerYear = 365.25;

    /// <summary>The three labels of the engine's own gross result, in order: costs, portfolio limits, sample (the engine's header, <c>SimulationHeader.FixedLabels</c>, delegates here). A run record takes the first from <see cref="CostDescription.Label"/>.</summary>
    public static IReadOnlyList<string> FixedLabels { get; } = Array.AsReadOnly([CostDescription.None, "portfolio limits: not simulated", "sample: in-sample"]);

    /// <summary>
    /// The labels of a run, in this order: exploration; fills; entry limit; costs (<see cref="CostDescription.Label"/> of the run's <see cref="SimulationRun.Costs"/>: <c>costs: none</c> for a gross run); the sizing line (a costed run only);
    /// portfolio limits; sample; universe; data; engine verification; tick; opening bar. A mirrored SELL adds <c>sell: mirrored (version)</c> after the sample line. The not-modelled list is <see cref="FillLimits.NotModelled"/> as it is for a gross run, followed by <see cref="CostLimits.NotModelled"/> for a costed one.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="rules"/> are not the rules the run recorded (<see cref="SimulationRun.Fills"/>).</exception>
    public static RunLabelSet Of(SimulationRun run, FillRules rules)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(rules);
        if (!string.Equals(rules.ToCanonicalJson(), run.Fills, StringComparison.Ordinal))
        {
            throw new ArgumentException("The fill rules are not the ones the run recorded; a label must say what the run did.", nameof(rules));
        }

        var entryLimit = FillDescription.Labels(rules)[1];
        string[] lines =
        [
            Exploration,
            rules.Slippage.IsNone ? FillsWithoutSlippage : Fills,
            entryLimit,
            CostDescription.Label(run.Costs),
            .. (run.Costs is null ? Array.Empty<string>() : [CostDescription.SizingLabel(run.Costs)]),
            FixedLabels[1],
            FixedLabels[2],
            .. SellLine(run.Strategy.DefinitionJson),
            .. TargetLine(run),
            .. SurpriseLine(run),
            .. RelativeStrengthLine(run),
            .. ReferenceLine(run),
            "universe: " + run.Universe.Label + SurvivorshipNote,
            DataLine(run),
            EngineLine(run.EngineVersion),
            TickLabel,
            OpeningBarLabel,
        ];

        return new RunLabelSet(Array.AsReadOnly(lines), run.Costs is null ? FillLimits.NotModelled : Array.AsReadOnly([.. FillLimits.NotModelled, .. CostLimits.NotModelled]));
    }

    /// <summary>
    /// The line <c>sell: mirrored (mirror-1)</c> for a run whose SELL side was made by the BUY to SELL mirror (spec P3-02e): the canonical definition carries <c>sell_mirrored</c>. No line for any other run.
    /// </summary>
    private static string[] SellLine(string? definitionJson)
    {
        // The canonical JSON has no white space. Scan it as JSON is read: strings (with their escapes) are skipped whole and only a member of the top-level object counts, so no text inside a condition can look like the member.
        const string Member = "sell_mirrored";
        if (definitionJson is null)
        {
            return [];
        }

        var depth = 0;
        for (var i = 0; i < definitionJson.Length; i++)
        {
            var c = definitionJson[i];
            if (c is '{' or '[')
            {
                depth++;
            }
            else if (c is '}' or ']')
            {
                depth--;
            }
            else if (c == '"')
            {
                var end = i + 1;
                while (end < definitionJson.Length && definitionJson[end] != '"')
                {
                    end += definitionJson[end] == '\\' ? 2 : 1;
                }

                if (depth == 1 && end - i - 1 == Member.Length && string.CompareOrdinal(definitionJson, i + 1, Member, 0, Member.Length) == 0
                    && end + 2 < definitionJson.Length && definitionJson[end + 1] == ':' && definitionJson[end + 2] == '"')
                {
                    var close = definitionJson.IndexOf('"', end + 3);
                    return close < 0 ? [] : ["sell: mirrored (" + definitionJson[(end + 3)..close] + ")"];
                }

                i = end;
            }
        }

        return [];
    }

    /// <summary>The id of the post-earnings-drift strategy (P15-09), whose <c>FIXED_RR</c> of 10 stands in for "no target" and whose target exits a run therefore counts and prints.</summary>
    public const string PeadStrategyId = "PEAD";

    /// <summary>The share of target exits above which a PEAD run is labelled "target not negligible" (P15-09): the stand-in target is then a real exit rule and the result is not a plain hold.</summary>
    public const double TargetNegligibleShare = 0.02;

    /// <summary>The target line of a run of <see cref="PeadStrategyId"/> (the count and share of exits by target; "target not negligible" above <see cref="TargetNegligibleShare"/> of the trades); no line for any other run.</summary>
    private static string[] TargetLine(SimulationRun run)
    {
        if (!string.Equals(run.Strategy.Id, PeadStrategyId, StringComparison.Ordinal))
        {
            return [];
        }

        var target = run.Trades.Count(t => t.Trade.ExitReason == ExitReason.Target);
        var share = run.Trades.Count == 0 ? 0.0 : target / (double)run.Trades.Count;
        var counts = string.Create(CultureInfo.InvariantCulture, $"{target} of {run.Trades.Count} trades left by the target ({share * 100:0.0} %)");
        return [share > TargetNegligibleShare ? "target not negligible: FIXED_RR 10 stands in for no target, and " + counts : "target: FIXED_RR 10 stands in for no target; " + counts];
    }

    /// <summary>The line of a run that read the relative-strength rows (BK-09): the baskets, their ids, "members as of", basis and parameter hash. No line for any other run.</summary>
    private static string[] RelativeStrengthLine(SimulationRun run) => run.RelativeStrength is { } r ? [r.Label()] : [];

    /// <summary>
    /// The lines of a run that read a quintile feature (P15-09): the filings whose <c>sue_yoy</c> rests on the synthetic parser (none when none does), the seasons whose breakpoints are NaN for lack of 100 values in the universe of the run, and the filings broadcast before the open of their
    /// own reaction session, which a rule on <c>days_since_results == 0</c> cannot enter. No line for any other run.
    /// </summary>
    private static string[] SurpriseLine(SimulationRun run)
    {
        if (run.ResultsDays is not { SurpriseSha256: not null } d)
        {
            return [];
        }

        var lines = new List<string>();
        if (d.SurpriseUnverified > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"surprise: sue_yoy rests on the synthetic filing parser for {d.SurpriseUnverified} of {d.SurpriseRows} filings (unverified until real filings pass); breakpoints from the previous complete season"));
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"surprise breakpoints: NaN in {d.SurpriseThinSeasons} of {d.SurpriseSeasons} seasons (fewer than 100 values in the run's universe)"));
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"reaction session missed: {d.MissedReactionFilings} of {d.ReactionFilings} filings were broadcast before the open of their reaction session, so days_since_results == 0 never fires for them"));
        return [.. lines];
    }

    /// <summary>
    /// The qualifier of a run whose definition uses a rule the oracle comparison (<see cref="VerifiedFor"/>) does not model: an <c>atr_mult</c> stop, the <c>event_exit</c> overlay or a hold over five sessions. Those rules have hand tables only (P15-09); no line for any other run.
    /// </summary>
    private static string[] ReferenceLine(SimulationRun run)
    {
        var json = run.Strategy.DefinitionJson ?? string.Empty;
        var outside = new List<string>();
        if (json.Contains("\"kind\":\"atr_mult\"", StringComparison.Ordinal))
        {
            outside.Add("atr_mult stop");
        }

        if (json.Contains("\"kind\":\"event_exit\"", StringComparison.Ordinal))
        {
            outside.Add("event_exit overlay");
        }

        var hold = System.Text.RegularExpressions.Regex.Match(json, "\"max_hold_sessions\":(\\d+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        if (hold.Success && int.Parse(hold.Groups[1].Value, CultureInfo.InvariantCulture) > 5)
        {
            outside.Add("hold over 5 sessions");
        }

        return outside.Count == 0 ? [] : ["rules outside the reference: " + string.Join(", ", outside) + " (hand tables only)"];
    }

    /// <summary>The data line from the instruments that ran: how many, the range of their first and of their last bars, and the shortest and longest length in years; <c>data: none</c> when none ran.</summary>
    private static string DataLine(SimulationRun run)
    {
        var ran = run.Instruments.Where(i => i.Skipped is null && i.FirstBar is not null && i.LastBar is not null).ToList();
        if (ran.Count == 0)
        {
            return "data: none";
        }

        var firstMin = ran.Min(i => i.FirstBar!.Value);
        var firstMax = ran.Max(i => i.FirstBar!.Value);
        var lastMin = ran.Min(i => i.LastBar!.Value);
        var lastMax = ran.Max(i => i.LastBar!.Value);
        var lengths = ran.Select(i => (i.LastBar!.Value.DayNumber - i.FirstBar!.Value.DayNumber) / DaysPerYear).ToList();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"data: {ran.Count} instruments; first bars {Date(firstMin)} to {Date(firstMax)}, last bars {Date(lastMin)} to {Date(lastMax)}; length from about {Years(lengths.Min())} to about {Years(lengths.Max())} years (days / 365.25, one decimal); no hold-out (ISS-19)");
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Years(double years) => Math.Round(years, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);
}
