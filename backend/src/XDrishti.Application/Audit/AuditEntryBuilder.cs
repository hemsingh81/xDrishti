using XDrishti.Application.Abstractions.Audit;
using XDrishti.Domain.Audit;

namespace XDrishti.Application.Audit;

/// <summary>Turns a stored row into the view: category and outcome from the kind table, the actor as a kind, every text redacted, the summary cut to 300. Pure.</summary>
internal static class AuditEntryBuilder
{
    public const int MaxSummaryLength = 300;

    public static AuditEntryView Build(AuditRaw raw, string? ownerEmail)
    {
        var (kind, shown) = AuditActors.Classify(raw.Source, raw.Actor, ownerEmail);
        var (text, textRedacted) = AuditRedactor.Redact(raw.Text);
        var (actor, actorRedacted) = AuditRedactor.Redact(shown);
        var label = AuditKinds.Label(raw.Source, raw.Kind);
        var summary = text.Length == 0 ? label : label + ": " + text;
        if (summary.Length > MaxSummaryLength)
        {
            summary = summary[..MaxSummaryLength];
        }

        return new AuditEntryView(
            AuditSources.Code(raw.Source) + ":" + raw.Id,
            raw.At,
            AuditSources.Code(raw.Source),
            AuditKinds.CategoryOf(raw.Source, raw.Kind),
            raw.Kind,
            AuditKinds.OutcomeOf(raw.Source, raw.Kind),
            kind,
            actor.Length == 0 ? null : actor,
            raw.AccountId,
            null,
            summary,
            textRedacted || actorRedacted,
            raw.PlanId,
            raw.ItemId);
    }

    /// <summary>The detail fields, each value redacted like any text.</summary>
    public static IReadOnlyList<AuditFieldView> Fields(AuditRaw raw) =>
        [.. raw.Fields.Select(f => new AuditFieldView(f.Label, AuditRedactor.Redact(f.Value).Text))];

    /// <summary>Whether the view matches the search text: only the redacted summary, the kind and the shown actor are searched, never the stored text.</summary>
    public static bool Matches(AuditEntryView view, AuditCriteria criteria) =>
        (criteria.Actors is null || criteria.Actors.Contains(view.ActorKind == AuditActors.OtherAddress ? AuditActors.Other : view.ActorKind))
        && (criteria.Text is null
            || view.Summary.Contains(criteria.Text, StringComparison.OrdinalIgnoreCase)
            || view.Kind.Contains(criteria.Text, StringComparison.OrdinalIgnoreCase)
            || (view.Actor?.Contains(criteria.Text, StringComparison.OrdinalIgnoreCase) ?? false));
}
