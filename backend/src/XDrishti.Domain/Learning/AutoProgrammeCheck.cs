using System.Globalization;
using System.Text;
using XDrishti.Domain.Execution.PhaseCheck;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The stored facts the AU programme check judges (spec AU-19). A null figure means the fact could not be read; zero means it was read and nothing is stored. Both are never a pass.
/// </summary>
public sealed record AutoProgrammeFacts(
    int? RecoveryPasses,
    int? MultiDayRecoveries,
    bool? RemovalAdrPresent,
    int? EssentialSettings,
    int? Jobs,
    int? FinishedJobs,
    int? FinishedJobChunks,
    int? FenceBreaches,
    int? SwitchChanges,
    int? AutoOnNightsWithSuggestions);

/// <summary>One exit item with its verdict (<see cref="PhaseVerdicts"/>) and the numbers behind it.</summary>
public sealed record AutoProgrammeItem(string Id, string Title, string Verdict, string Detail);

/// <summary>The programme verdict: MET only when every item is MET, NOT MET when any item is, NOT EVALUABLE otherwise.</summary>
public sealed record AutoProgrammeReport(string Verdict, IReadOnlyList<AutoProgrammeItem> Items);

/// <summary>The pure judgement of the AU exit items (spec AU-19, ADR 0253). Nothing here reads anything; a missing fact is NOT EVALUABLE and never MET.</summary>
public static class AutoProgrammeCheck
{
    /// <summary>The most Essential settings the programme allows (spec AU-08).</summary>
    public const int MaxEssential = 8;

    /// <summary>The newest jobs the check looks at; with this many and none finished, older jobs are unseen.</summary>
    // shortcut: newest 50 jobs only, upgrade when job history grows
    public const int JobWindow = 50;

    public static AutoProgrammeReport Evaluate(AutoProgrammeFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        AutoProgrammeItem[] items =
        [
            Recovery(facts),
            Removal(facts),
            Essential(facts),
            Job(facts),
            Suggestions(facts),
        ];
        var verdict = items.Any(i => i.Verdict == PhaseVerdicts.NotMet) ? PhaseVerdicts.NotMet
            : items.All(i => i.Verdict == PhaseVerdicts.Met) ? PhaseVerdicts.Met
            : PhaseVerdicts.NotEvaluable;
        return new AutoProgrammeReport(verdict, items);
    }

    public static string Render(AutoProgrammeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"AU programme check: {report.Verdict}");
        foreach (var item in report.Items)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {item.Id} {item.Title}: {item.Verdict} - {item.Detail}");
        }

        text.AppendLine("Limits: read only; an unreadable or empty fact is NOT EVALUABLE, never MET. Auto suggests only and places no order. An automatic decision is never an owner approval.");
        return text.ToString();
    }

    private static AutoProgrammeItem Recovery(AutoProgrammeFacts f)
    {
        const string title = "A real multi-day recovery in the ledger";
        if (f.RecoveryPasses is not { } passes || f.MultiDayRecoveries is not { } multi)
        {
            return Item("recovery", title, PhaseVerdicts.NotEvaluable, "the recovery ledger could not be read");
        }

        if (passes == 0)
        {
            return Item("recovery", title, PhaseVerdicts.NotEvaluable, "no recovery pass is recorded");
        }

        return multi > 0
            ? Item("recovery", title, PhaseVerdicts.Met, $"{multi} succeeded item(s) cover more than one session, from {passes} pass(es)")
            : Item("recovery", title, PhaseVerdicts.NotMet, $"{passes} pass(es) recorded, none succeeded over more than one session");
    }

    private static AutoProgrammeItem Removal(AutoProgrammeFacts f)
    {
        const string title = "The removal ADR is present";
        return f.RemovalAdrPresent switch
        {
            true => Item("removal", title, PhaseVerdicts.Met, "ADR 0245 is in the ADR folder"),
            false => Item("removal", title, PhaseVerdicts.NotMet, "ADR 0245 is not in the ADR folder"),
            null => Item("removal", title, PhaseVerdicts.NotEvaluable, "the ADR folder could not be read here"),
        };
    }

    private static AutoProgrammeItem Essential(AutoProgrammeFacts f)
    {
        var title = $"At most {MaxEssential} Essential settings";
        return f.EssentialSettings switch
        {
            null or 0 => Item("essential", title, PhaseVerdicts.NotEvaluable, "the settings catalog has no Essential entry to count"),
            var n when n <= MaxEssential => Item("essential", title, PhaseVerdicts.Met, $"{n} Essential"),
            var n => Item("essential", title, PhaseVerdicts.NotMet, $"{n} Essential"),
        };
    }

    private static AutoProgrammeItem Job(AutoProgrammeFacts f)
    {
        const string title = "A finished simulation job and its evidence fence";
        if (f.Jobs is not { } jobs || f.FinishedJobs is not { } finished || f.FinishedJobChunks is not { } chunks || f.FenceBreaches is not { } breaches)
        {
            return Item("job", title, PhaseVerdicts.NotEvaluable, "the job tables could not be read");
        }

        if (jobs == 0)
        {
            return Item("job", title, PhaseVerdicts.NotEvaluable, "no simulate-everything job is recorded");
        }

        if (breaches > 0)
        {
            return Item("job", title, PhaseVerdicts.NotMet, $"{breaches} finished chunk(s) are not training runs cut before the hold-out");
        }

        if (finished == 0 && jobs >= JobWindow)
        {
            return Item("job", title, PhaseVerdicts.NotEvaluable, $"none of the {JobWindow} newest jobs finished; older jobs are not looked at");
        }

        return finished > 0 && chunks > 0
            ? Item("job", title, PhaseVerdicts.Met, $"{finished} finished job(s), {chunks} finished chunk(s), all training runs cut before the hold-out")
            : Item("job", title, PhaseVerdicts.NotMet, $"{jobs} job(s) among the {JobWindow} newest, none finished with a finished chunk");
    }

    private static AutoProgrammeItem Suggestions(AutoProgrammeFacts f)
    {
        const string title = "A night of suggestions with Auto ON";
        if (f.SwitchChanges is not { } changes || f.AutoOnNightsWithSuggestions is not { } nights)
        {
            return Item("suggestions", title, PhaseVerdicts.NotEvaluable, "the Auto switch log or the plans could not be read");
        }

        if (changes == 0)
        {
            return Item("suggestions", title, PhaseVerdicts.NotEvaluable, "the Auto switch has never been changed, so no night was run with it ON");
        }

        return nights > 0
            ? Item("suggestions", title, PhaseVerdicts.Met, $"{nights} published night(s) with suggestions while Auto was ON")
            : Item("suggestions", title, PhaseVerdicts.NotMet, "no published night with suggestions while Auto was ON");
    }

    private static AutoProgrammeItem Item(string id, string title, string verdict, string detail) => new(id, title, verdict, detail);
}
