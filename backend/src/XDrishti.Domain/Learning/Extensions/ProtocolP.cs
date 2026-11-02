using System.Globalization;
using XDrishti.Domain.Common;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// Protocol P of P15-10 (post-earnings drift): four counted variants (surprise measure times hold). The variant is chosen on the first eight seasons and frozen; the test seasons then judge the top bucket against the middle three on the same dates, so general drift and market moves
/// cancel. KEEP needs at least eight test seasons, 250 matched top-bucket events, a season-cluster lower bound of the gain at or above 0.05 R times the factor and a positive gain in two thirds of the test seasons. The hold-out (last four seasons) is read only by <see cref="HoldOut"/>
/// and never by <see cref="Evaluate"/>, which uses its dates and sessions to cut the test and nothing else. Fewer than 20 seasons is NOT_EVALUABLE with the seasons named. Pure and seeded.
/// </summary>
public static class ProtocolP
{
    /// <summary>The protocol letter.</summary>
    public const string Protocol = "P";

    /// <summary>The four counted variants: measure and hold in sessions.</summary>
    public static IReadOnlyList<string> Variants { get; } = ["sue_yoy/5", "sue_yoy/20", "results_car3/5", "results_car3/20"];

    private sealed record Delta(DateOnly Date, int ExitSession, double Value);

    /// <summary>The verdict over all events (the hold-out events are used for their seasons and first session only). <paramref name="variants"/> is N of the ledger.</summary>
    public static Result<ProtocolPResult> Evaluate(IReadOnlyList<PeadEvent> events, int variants, ProtocolPOptions options)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(options);
        var problems = options.Problems().ToList();
        if (variants < 1)
        {
            problems.Add("the ledger count is at least 1");
        }

        if (events.Any(e => e is null || !Variants.Contains(e.Variant) || !double.IsFinite(e.R) || e.ExitSession < e.EntrySession))
        {
            problems.Add("every event needs one of the four variants, a finite R and an exit not before its entry");
        }

        if (problems.Count > 0)
        {
            return Result.Failure<ProtocolPResult>(Error.Validation("extensions.options", string.Join("; ", problems)));
        }

        var c = CultureInfo.InvariantCulture;
        string[] limits =
        [
            "Gross R unless the owner's checked charge sheet exists (ISS-25); about 0.35 % a round trip of delivery costs is an UNVERIFIED planning figure.",
            "The comparison is the top quintile against the middle three on the same dates; the bottom quintile is research only.",
            "Advisory research only: a KEEP is a record.",
        ];
        var seasons = SeasonFolds.Distinct(events.Select(e => e.EntryDate));
        if (seasons.Count < options.RequiredSeasons)
        {
            var empty = EvidenceBlock.NotEvaluable(Protocol, [string.Create(c, $"seasons {seasons.Count} of {options.RequiredSeasons}")], limits, events.Count, seasons.Count);
            return Result.Success(new ProtocolPResult(empty, null, null));
        }

        var (choice, test, holdOut) = SeasonFolds.Split(seasons, options.ChoiceSeasons);
        var factor = MultipleTesting.Factor(variants);
        var gain = options.MinGainR * factor;
        var firstTest = events.Where(e => test.Contains(Season.Of(e.EntryDate))).Select(e => (int?)e.EntrySession).Min();
        var firstHold = events.Where(e => holdOut.Contains(Season.Of(e.EntryDate))).Select(e => (int?)e.EntrySession).Min();

        // The choice: the variant with the best gain on the choice seasons, events purged where they run into the test.
        string? chosen = null;
        double? chosenGain = null;
        foreach (var variant in Variants)
        {
            var deltas = Deltas(events, variant, choice, firstTest, options.EmbargoSessions);
            if (deltas.Count > 0 && deltas.Average(d => d.Value) is var g && (chosenGain is null || g > chosenGain))
            {
                (chosen, chosenGain) = (variant, g);
            }
        }

        var missing = new List<string>();
        if (chosen is null)
        {
            missing.Add("matched top-bucket events in the choice seasons 0");
        }

        var testDeltas = chosen is null ? [] : Deltas(events, chosen, test, firstHold, options.EmbargoSessions);
        var testSeasons = testDeltas.Select(d => Season.Of(d.Date)).Distinct().Count();
        if (chosen is not null)
        {
            if (testSeasons < options.MinTestSeasons)
            {
                missing.Add(string.Create(c, $"test seasons {testSeasons} of {options.MinTestSeasons}"));
            }

            if (testDeltas.Count < options.MinTopEvents)
            {
                missing.Add(string.Create(c, $"top-bucket events {testDeltas.Count} of {options.MinTopEvents}"));
            }
        }

        BootstrapBounds? bounds = null;
        var votes = new List<(Season Season, int Events, double Gain)>();
        if (chosen is not null)
        {
            var groups = testDeltas.GroupBy(d => Season.Of(d.Date)).OrderBy(g => g.Key).ToArray();
            votes = [.. groups.Select(g => (g.Key, g.Count(), g.Average(d => d.Value)))];
            if (missing.Count == 0)
            {
                bounds = BlockBootstrap.Bounds([.. groups.Select(g => new BootstrapBlock(g.Sum(d => d.Value), g.Count()))], options.Draws, options.Seed, options.Level);
            }
        }

        var failed = new List<string>();
        var positive = votes.Count(v => v.Gain > 0);
        var needed = (int)Math.Ceiling((votes.Count * options.PositiveSeasonShare) - 1e-9);
        var verdict = ExtensionVerdict.NotEvaluable;
        if (missing.Count == 0)
        {
            if (!(bounds!.Lower >= gain))
            {
                failed.Add(string.Create(c, $"season-cluster lower bound {Fmt(bounds.Lower)} R below {gain:F3} R"));
            }

            if (positive < needed)
            {
                failed.Add(string.Create(c, $"gain above zero in {positive} of {votes.Count} test seasons, {needed} needed"));
            }

            verdict = VerdictTable.Decide(missing, failed, bounds.Upper is { } upper && upper < options.MinGainR);
        }

        using var estimates = new ExtensionJson();
        estimates.Object().Count("seasons", seasons.Count).Count("choice_seasons", choice.Count).Count("test_seasons", testSeasons).Count("hold_out_seasons", holdOut.Count).Text("chosen_variant", chosen)
            .Number("choice_gain_r", chosenGain).Count("test_top_events", testDeltas.Count).Number("test_gain_r", bounds?.Point).Number("lower", bounds?.Lower).Number("upper", bounds?.Upper).Number("required", missing.Count == 0 ? gain : null)
            .Count("positive_seasons", positive).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Count("ledger_trials", variants).Number("factor", factor).Text("frozen_variant", chosen).Array("test_seasons");
        foreach (var v in votes)
        {
            evidence.Object().Text("season", v.Season.ToString()).Count("top_events", v.Events).Number("gain_r", v.Gain).EndObject();
        }

        evidence.EndArray();
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        var outcome = new ProtocolOutcome(verdict, missing, failed, limits, estimates.Finish(), evidence.Finish(), votes.Count, votes.Count, testDeltas.Count);
        return Result.Success(new ProtocolPResult(outcome, chosen, bounds?.Point));
    }

    /// <summary>The one reading of the hold-out for the frozen variant: KEEP when the gain has the sign of the test gain, INCONCLUSIVE when not, NOT_EVALUABLE with too few matched top-bucket events.</summary>
    public static Result<ProtocolOutcome> HoldOut(IReadOnlyList<PeadEvent> events, string chosenVariant, double testGainR, ProtocolPOptions options)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(chosenVariant);
        ArgumentNullException.ThrowIfNull(options);
        if (!Variants.Contains(chosenVariant) || !double.IsFinite(testGainR) || options.Problems().Count > 0)
        {
            return Result.Failure<ProtocolOutcome>(Error.Validation("extensions.options", "The hold-out needs one of the four variants, a finite test gain and valid options."));
        }

        var seasons = SeasonFolds.Distinct(events.Select(e => e.EntryDate));
        var hold = SeasonFolds.Split(seasons, options.ChoiceSeasons).HoldOut;
        var deltas = Deltas(events, chosenVariant, hold, null, 0);
        var missing = new List<string>();
        if (deltas.Count < options.MinHoldOutEvents)
        {
            missing.Add(string.Create(CultureInfo.InvariantCulture, $"hold-out top-bucket events {deltas.Count} of {options.MinHoldOutEvents}"));
        }

        double? gain = deltas.Count > 0 ? deltas.Average(d => d.Value) : null;
        var failed = new List<string>();
        if (missing.Count == 0 && !(gain != 0 && Math.Sign(gain!.Value) == Math.Sign(testGainR)))
        {
            failed.Add("the hold-out gain does not have the sign of the test gain");
        }

        var verdict = missing.Count > 0 ? ExtensionVerdict.NotEvaluable : failed.Count == 0 ? ExtensionVerdict.Keep : ExtensionVerdict.Inconclusive;
        using var estimates = new ExtensionJson();
        estimates.Object().Text("chosen_variant", chosenVariant).Count("hold_out_top_events", deltas.Count).Number("hold_out_gain_r", missing.Count == 0 ? gain : null).Number("test_gain_r", testGainR).EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", Protocol).Text("read", "hold-out, once");
        EvidenceBlock.Close(evidence, verdict, missing, failed);
        return Result.Success(new ProtocolOutcome(verdict, missing, failed, ["The hold-out is read once; this record is that reading.", "Advisory research only: a KEEP is a record."], estimates.Finish(), evidence.Finish(), hold.Count, 0, deltas.Count));
    }

    // Each top-bucket event of the variant in the given seasons, as its R minus the mean R of the middle events on the same entry date; dates without a middle event match nothing. Events that run into the next phase are purged.
    private static List<Delta> Deltas(IReadOnlyList<PeadEvent> events, string variant, IReadOnlyList<Season> seasons, int? nextPhaseFirstSession, int embargo)
    {
        var inside = events
            .Where(e => e.Variant == variant && seasons.Contains(Season.Of(e.EntryDate)) && !(nextPhaseFirstSession is { } next && e.ExitSession + embargo >= next))
            .OrderBy(e => e.EntryDate).ThenBy(e => e.Key, StringComparer.Ordinal).ToArray();
        var deltas = new List<Delta>();
        foreach (var day in inside.GroupBy(e => e.EntryDate))
        {
            var middle = day.Where(e => e.Bucket == PeadBucket.Middle).ToArray();
            if (middle.Length == 0)
            {
                continue;
            }

            var mean = middle.Sum(e => e.R) / middle.Length;
            deltas.AddRange(day.Where(e => e.Bucket == PeadBucket.Top).Select(e => new Delta(e.EntryDate, e.ExitSession, e.R - mean)));
        }

        return deltas;
    }

    private static string Fmt(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";
}
