using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>One knob the screen may offer for a stored baseline (spec P8-18, decision D3).</summary>
/// <param name="Kind"><c>exit</c> or <c>slippage</c>; never <c>parameter</c>.</param>
/// <param name="Name">The knob: <c>FIXED_RR.rr</c> for an exit parameter, <c>slippage.ticks</c> for a slippage component.</param>
/// <param name="Current">The baseline's value.</param>
/// <param name="Min">The lowest value the knob accepts (for slippage: the baseline value, a cheaper assumption cannot win).</param>
/// <param name="Max">The highest value.</param>
/// <param name="Note">One sentence for the form.</param>
public sealed record KnobOffer(ExperimentKnobKind Kind, string Name, double Current, double Min, double Max, string Note);

/// <summary>A knob that passed <see cref="ReplayKnobCatalogue.Check"/>: the experiment knob with its two values as text, and the numbers.</summary>
/// <param name="Knob">The knob as the experiment ledger takes it.</param>
/// <param name="Current">The baseline value.</param>
/// <param name="Candidate">The candidate value.</param>
public sealed record CheckedKnob(ExperimentKnob Knob, double Current, double Candidate);

/// <summary>
/// The knobs a replay started from the screen may change (spec P8-18, decision D3; ADR 0128): <b>exit parameters</b> that the exit policy of the baseline has as one plain number, and <b>slippage upward</b>. Both leave the entries as they are (an exit replaces only the
/// exits; slippage only the fill price), which is what makes the replay comparable (ADR 0072 decision 1). A strategy <c>parameter</c> is refused: nothing says it does not change which entries exist. Learning-side knobs are not replays and are not here. Pure: the ranges are
/// those the strategy compile already enforces.
/// </summary>
public static class ReplayKnobCatalogue
{
    /// <summary>The refusal of a knob that is not allowed (the answer is 422 <c>experiments.knob_not_allowed</c>).</summary>
    public const string NotAllowedCode = "experiments.knob_not_allowed";

    private sealed record Range(string Name, double Min, bool MinInclusive, double Max);

    private static readonly Dictionary<string, Range[]> ExitRanges = new(StringComparer.Ordinal)
    {
        ["FIXED_RR"] = [new("rr", 0, false, 10)],
        ["PARTIAL_BE"] = [new("book_pct", 0, false, 100), new("t1_rr", 0, false, 10), new("t2_rr", 0, false, 10)],
        ["PARTIAL_TRAIL"] = [new("book_pct", 0, false, 100), new("t1_rr", 0, false, 10), new("trail_atr_mult", 0, false, 20)],
        ["ATR_TRAIL"] = [new("mult", 0, false, 20), new("activate_at_rr", 0, true, 10)],
    };

    private static readonly (string Name, double Max)[] SlippageComponents = [("slippage.ticks", SlippageConfig.MaxTicks), ("slippage.bar_range_frac", 1), ("slippage.stop_exit_extra_ticks", SlippageConfig.MaxTicks)];

    /// <summary>The knobs offered for a baseline whose exit is <paramref name="exit"/> (null when it cannot be read as one exit) and whose slippage is <paramref name="slippage"/>.</summary>
    public static IReadOnlyList<KnobOffer> Offers(ExitCandidate? exit, SlippageConfig slippage)
    {
        ArgumentNullException.ThrowIfNull(slippage);
        var offers = new List<KnobOffer>();
        if (exit is not null && ExitRanges.TryGetValue(exit.Policy, out var ranges))
        {
            foreach (var range in ranges)
            {
                if (ExitValue(exit, range.Name) is { } current)
                {
                    offers.Add(new KnobOffer(ExperimentKnobKind.Exit, exit.Policy + "." + range.Name, current, range.Min, range.Max, "An exit parameter: the entries stay the same, only the exit changes."));
                }
            }
        }

        foreach (var (name, max) in SlippageComponents)
        {
            var current = Slippage(slippage, name);
            offers.Add(new KnobOffer(ExperimentKnobKind.Slippage, name, current, current, max, "Slippage upward only: a cheaper cost assumption cannot win (doc 09 §12 rule 3)."));
        }

        return offers;
    }

    /// <summary>Checks a knob a caller asked for against the baseline; the failure is <see cref="NotAllowedCode"/> with the reason.</summary>
    public static Result<CheckedKnob> Check(string? kind, string? name, double candidate, ExitCandidate? exit, SlippageConfig slippage)
    {
        ArgumentNullException.ThrowIfNull(slippage);
        if (!double.IsFinite(candidate))
        {
            return Refuse("the candidate value must be a finite number");
        }

        if (string.Equals(kind, "parameter", StringComparison.OrdinalIgnoreCase))
        {
            return Refuse("a strategy parameter may change which entries exist, so its replay is not comparable; it stays a verb (xd simulate, xd learn experiment)");
        }

        if (string.Equals(kind, "slippage", StringComparison.Ordinal))
        {
            return CheckSlippage(name, candidate, slippage);
        }

        if (string.Equals(kind, "exit", StringComparison.Ordinal))
        {
            return CheckExit(name, candidate, exit);
        }

        return Refuse("the kind of knob must be exit or slippage");
    }

    private static Result<CheckedKnob> CheckSlippage(string? name, double candidate, SlippageConfig slippage)
    {
        var component = SlippageComponents.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (component.Name is null)
        {
            return Refuse("the slippage knobs are " + string.Join(", ", SlippageComponents.Select(c => c.Name)));
        }

        var current = Slippage(slippage, component.Name);
        if (candidate < current)
        {
            return Refuse("a cheaper cost assumption cannot win (doc 09 §12 rule 3)");
        }

        if (candidate == current)
        {
            return Refuse("the candidate value is the baseline value: nothing is changed");
        }

        return candidate > component.Max
            ? Refuse(component.Name + " is at most " + CanonicalText.Number(component.Max))
            : Result.Success(Make(ExperimentKnobKind.Slippage, component.Name, current, candidate));
    }

    private static Result<CheckedKnob> CheckExit(string? name, double candidate, ExitCandidate? exit)
    {
        if (exit is null)
        {
            return Refuse("the baseline's exit cannot be read as one exit with plain numbers");
        }

        var dot = name?.IndexOf('.', StringComparison.Ordinal) ?? -1;
        var policy = dot > 0 ? name![..dot] : null;
        var parameter = dot > 0 ? name![(dot + 1)..] : null;
        if (!string.Equals(policy, exit.Policy, StringComparison.Ordinal) || !ExitRanges.TryGetValue(exit.Policy, out var ranges) || ranges.FirstOrDefault(r => string.Equals(r.Name, parameter, StringComparison.Ordinal)) is not { } range || ExitValue(exit, range.Name) is not { } current)
        {
            return Refuse("the exit knobs of this baseline are " + string.Join(", ", Offers(exit, SlippageConfig.None).Where(o => o.Kind == ExperimentKnobKind.Exit).Select(o => o.Name)));
        }

        if (ExperimentKnob.SameValue(CanonicalText.Number(candidate), CanonicalText.Number(current)))
        {
            return Refuse("the candidate value is the baseline value: nothing is changed");
        }

        if (candidate > range.Max || candidate < range.Min || (candidate == range.Min && !range.MinInclusive))
        {
            return Refuse(name + " must be " + (range.MinInclusive ? "from " : "above ") + CanonicalText.Number(range.Min) + " to " + CanonicalText.Number(range.Max));
        }

        if (exit.Policy == "PARTIAL_BE" && Relation(exit, range.Name, candidate) is { } broken)
        {
            return Refuse(broken);
        }

        return Result.Success(Make(ExperimentKnobKind.Exit, name!, current, candidate));
    }

    // PARTIAL_BE needs t2_rr above t1_rr; the compile would refuse a pair that breaks it, so it is refused before a job is queued.
    private static string? Relation(ExitCandidate exit, string changed, double candidate)
    {
        var t1 = changed == "t1_rr" ? candidate : ExitValue(exit, "t1_rr");
        var t2 = changed == "t2_rr" ? candidate : ExitValue(exit, "t2_rr");
        return changed is "t1_rr" or "t2_rr" && t1 is { } a && t2 is { } b && b <= a ? "t2_rr must stay above t1_rr" : null;
    }

    private static CheckedKnob Make(ExperimentKnobKind kind, string name, double current, double candidate) =>
        new(new ExperimentKnob(name, kind, CanonicalText.Number(current), CanonicalText.Number(candidate)), current, candidate);

    private static double? ExitValue(ExitCandidate exit, string name) =>
        exit.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)) is { Values: [var single] } && double.IsFinite(single) ? single : null;

    private static double Slippage(SlippageConfig slippage, string name) => name switch
    {
        "slippage.ticks" => slippage.Ticks,
        "slippage.bar_range_frac" => slippage.BarRangeFrac,
        _ => slippage.StopExitExtraTicks,
    };

    private static Result<CheckedKnob> Refuse(string reason) =>
        Result.Failure<CheckedKnob>(Error.Validation(NotAllowedCode, "This knob is not allowed: " + reason + "."));
}
