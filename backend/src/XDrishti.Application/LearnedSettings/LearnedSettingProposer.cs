using System.Globalization;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Application.LearnedSettings;

/// <summary>What the proposer says about one piece of evidence.</summary>
public enum LearnedOutcome
{
    /// <summary>No change; <see cref="LearnedDecision.Code"/> says why.</summary>
    Skip,

    /// <summary>A move in the safer direction (may be applied by itself when Auto is ON).</summary>
    Tighten,

    /// <summary>A move the other way: only ever a proposal, applied only with the owner's fresh password.</summary>
    Loosen,
}

/// <summary>The proposer's answer. <c>NewValue</c> is meaningful only when the outcome is not <see cref="LearnedOutcome.Skip"/>.</summary>
public sealed record LearnedDecision(LearnedOutcome Outcome, string Code, decimal NewValue)
{
    public static LearnedDecision Skipped(string code) => new(LearnedOutcome.Skip, code, 0m);
}

/// <summary>
/// The pure rule of AU-10 (ADR 0242): given a descriptor, the value in force and one piece of evidence, say whether to leave the setting, tighten it by one bounded step, or propose a loosening. Every doubt is a skip: no descriptor, not allow-listed, not
/// learnable, no declared direction, evidence unavailable, not out of sample, from sealed hold-out data, outside the bounds, or no change. No I/O, no clock.
/// </summary>
public static class LearnedSettingProposer
{
    /// <summary>The actor recorded for a change learning applied by itself. It is never an owner approval.</summary>
    public const string AutoActor = "auto";

    public static LearnedDecision Propose(SettingDescriptor? descriptor, string? currentText, LearnedEvidenceItem? evidence, int maxStepPercent, string? configuredText = null, int maxTotalPercent = 100)
    {
        if (evidence is null)
        {
            return LearnedDecision.Skipped("learned.evidence_unavailable");
        }

        if (descriptor is null || !descriptor.Learnable || !LearnedAllowList.Keys.Contains(descriptor.Key) || descriptor.Kind != SettingKind.Configuration)
        {
            return LearnedDecision.Skipped("learned.not_learnable");
        }

        if (descriptor.Safer == SettingSafer.None)
        {
            return LearnedDecision.Skipped("learned.no_direction");
        }

        if (evidence.FromSealedHoldOut)
        {
            return LearnedDecision.Skipped("learned.sealed_evidence");
        }

        if (!evidence.OutOfSample)
        {
            return LearnedDecision.Skipped("learned.not_out_of_sample");
        }

        if (string.IsNullOrWhiteSpace(evidence.Hash) || string.IsNullOrWhiteSpace(evidence.Reference) || !TryNumber(currentText, out var current) || !TryNumber(evidence.ProposedValue, out var proposed))
        {
            return LearnedDecision.Skipped("learned.evidence_unavailable");
        }

        if (!descriptor.InRange(proposed) || !descriptor.InRange(current))
        {
            return LearnedDecision.Skipped("learned.out_of_range");
        }

        if (proposed == current)
        {
            return LearnedDecision.Skipped("learned.no_change");
        }

        if (descriptor.IsLoosening(current, proposed))
        {
            return new LearnedDecision(LearnedOutcome.Loosen, "learned.loosening", proposed);
        }

        var step = Math.Abs(current) * Math.Clamp(maxStepPercent, 1, 25) / 100m;
        if (step == 0m)
        {
            return LearnedDecision.Skipped("learned.step_zero");
        }

        var stepped = descriptor.Safer == SettingSafer.Higher ? Math.Min(proposed, current + step) : Math.Max(proposed, current - step);
        if (TryNumber(configuredText, out var configured))
        {
            // total drift from the owner's configured value: stacked steps cannot walk a gate away without limit
            var room = Math.Abs(configured) * Math.Clamp(maxTotalPercent, 1, 100) / 100m;
            stepped = descriptor.Safer == SettingSafer.Higher ? Math.Min(stepped, configured + room) : Math.Max(stepped, configured - room);
        }

        stepped = Math.Round(stepped, 6);
        if (stepped == current)
        {
            return LearnedDecision.Skipped("learned.total_limit");
        }

        if (!descriptor.InRange(stepped) || descriptor.IsLoosening(current, stepped))
        {
            return LearnedDecision.Skipped("learned.no_change");
        }

        return new LearnedDecision(LearnedOutcome.Tighten, "learned.tightening", stepped);
    }

    /// <summary>Invariant text of a number, as stored.</summary>
    public static string Text(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static bool TryNumber(string? text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
