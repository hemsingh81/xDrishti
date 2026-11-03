using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>
/// Protocol P (P15-10): reads the PEAD events through the port, counts the four variants in the ledger before any number is computed (when there are events), chooses and freezes the variant on the first seasons, judges the test seasons and stores the record, with the chosen variant in it.
/// With today's data there are no events and the answer is NOT_EVALUABLE with the seasons named. The hold-out is read only by the explicit act, after a stored KEEP, once.
/// </summary>
internal sealed class PeadEvaluation(IPeadEventSource events, EvaluationWriter writer, IExtensionEvaluationStore store, ILogger<PeadEvaluation> logger)
{
    private const string Method = "pead-compare";
    private const string VersionKey = "pead-1";

    public async ValueTask<Result<ExtensionEvaluationOutcome>> RunAsync(bool holdOut, CancellationToken cancellationToken)
    {
        var options = new ProtocolPOptions();

        // A spent hold-out refuses before the variants are counted or anything is written: a refused --holdout must not raise N.
        // shortcut: check-then-store is not atomic across two concurrent runs of the verb; the CLI has one owner and the table's unique index is the backstop. Add a per-family advisory lock if a second caller ever exists.
        if (holdOut && await store.HoldOutUsedAsync(ExtensionFamilies.Pead, VersionKey, cancellationToken))
        {
            ExtensionEvaluationLog.Refused(logger, ExtensionFamilies.Pead, ExtensionEvaluationErrors.HoldOutUsed);
            return Result.Failure<ExtensionEvaluationOutcome>(Error.Conflict(ExtensionEvaluationErrors.HoldOutUsed, "The hold-out of " + ExtensionFamilies.Pead + " for " + VersionKey + " has been read already; it is read once."));
        }

        var set = await events.ReadAsync(cancellationToken);
        var all = set.Events;
        var variants = await writer.CountAsync(ExtensionFamilies.Pead, cancellationToken);
        ExtensionTrialId? chosenTrial = null;
        if (all.Count > 0)
        {
            var from = all.Min(e => e.EntryDate);
            var to = all.Max(e => e.EntryDate);
            var eventsHash = ExtensionFamilies.Sha256Hex(string.Join(',', all.Select(e => e.Key).Order(StringComparer.Ordinal)));
            foreach (var variant in ProtocolP.Variants)
            {
                variants = await writer.AppendTrialAsync(ExtensionFamilies.TrialOf(ExtensionFamilies.Pead, TrialText(variant, options), from, to), eventsHash, from, to, Method, cancellationToken);
            }
        }

        var evaluated = ProtocolP.Evaluate(all, variants, options);
        if (evaluated.IsFailure)
        {
            return Result.Failure<ExtensionEvaluationOutcome>(evaluated.Error);
        }

        var result = evaluated.Value;
        if (result.ChosenVariant is { } chosen && all.Count > 0)
        {
            chosenTrial = ExtensionFamilies.TrialOf(ExtensionFamilies.Pead, TrialText(chosen, options), all.Min(e => e.EntryDate), all.Max(e => e.EntryDate));
        }

        var devRecord = Identity(variants, chosenTrial, set, result.ChosenVariant, false, null).ToRecord(result.Outcome);

        // Everything is computed before anything is written: a refused second reading leaves no record.
        ExtensionEvaluationRecord? holdRecord = null;
        string? note = null;
        if (holdOut)
        {
            if (result.Outcome.Verdict != ExtensionVerdict.Keep || result.ChosenVariant is not { } frozen || result.TestGainR is not { } gain)
            {
                note = "The hold-out was not read: the development verdict is not KEEP.";
                ExtensionEvaluationLog.HoldOutNotRead(logger, ExtensionFamilies.Pead, "no_development_keep");
            }
            else
            {
                var hold = ProtocolP.HoldOut(all, frozen, gain, options);
                if (hold.IsFailure)
                {
                    return Result.Failure<ExtensionEvaluationOutcome>(hold.Error);
                }

                holdRecord = Identity(variants, chosenTrial, set, frozen, true, devRecord.EvaluationId).ToRecord(hold.Value);
            }
        }

        var records = new List<EvaluatedRecord>();
        foreach (var record in holdRecord is null ? [devRecord] : new[] { devRecord, holdRecord })
        {
            var stored = await writer.StoreAsync(record, cancellationToken);
            if (stored.IsFailure)
            {
                return Result.Failure<ExtensionEvaluationOutcome>(stored.Error);
            }

            ExtensionEvaluationLog.Stored(logger, record.Family, record.Protocol, record.VersionKey, record.Verdict, record.Rows, stored.Value.Stored);
            records.Add(stored.Value);
        }

        return Result.Success(new ExtensionEvaluationOutcome(ExtensionFamilies.Pead, records, note));
    }

    private static ProtocolIdentity Identity(int variants, ExtensionTrialId? trial, PeadEventSet set, string? chosen, bool holdOut, string? developmentRecord) => new(
        ExtensionFamilies.Pead,
        ProtocolP.Protocol,
        null,
        VersionKey,
        "none",
        ExtensionFamilies.Sha256Hex(new ProtocolPOptions().Canonical()),
        null,
        trial?.Hash,
        variants,
        "quarter",
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["engine"] = set.Engine ?? "none",
            ["events"] = set.Events.Count.ToString(CultureInfo.InvariantCulture),
            ["events_sha256"] = ExtensionFamilies.Sha256Hex(string.Join(',', set.Events.Select(e => e.Key).Order(StringComparer.Ordinal))),
            ["frozen_variant"] = chosen ?? "none",
            ["source"] = set.SourceNote,
            ["development_record"] = developmentRecord ?? "none",
        },
        holdOut);

    private static string TrialText(string variant, ProtocolPOptions options) => string.Join(
        '\n',
        "pead-trial/1",
        "variant:" + variant,
        "buckets:top quintile against the middle three on the same dates; entry at the close of the reaction session",
        "folds:season=calendar quarter; choice first " + options.ChoiceSeasons.ToString(CultureInfo.InvariantCulture) + ", hold-out last " + SeasonFolds.HoldOutSeasons.ToString(CultureInfo.InvariantCulture),
        "options:" + options.Canonical());
}
