using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// A split and a bonus issue on the same ex-date of one instrument is almost always a detection that guessed the wrong kind (or a second entry of the same event). Applying one kind while the other exists
/// for that instrument and date: a suggestion of the other kind is declined by itself (it was only a guess), an applied one blocks (<c>data.corporate_action_conflicts_same_day</c>, 409) until the owner
/// ignores it. Ignored actions do not count.
/// </summary>
internal static class SameDayActions
{
    /// <summary>The not ignored actions of the instrument on the date that are of the other kind than <paramref name="kind"/> (the action being edited is left out).</summary>
    public static List<CorporateAction> OthersOn(IEnumerable<CorporateAction> notIgnored, Guid instrumentId, DateOnly exDate, CorporateActionKind kind, Guid? except) =>
        [.. notIgnored.Where(a => a.InstrumentId == instrumentId && a.ExDate == exDate && a.Kind != kind && a.Id != except)];

    /// <summary>True when one of the others is applied: the new action must be refused.</summary>
    public static bool Blocks(IReadOnlyCollection<CorporateAction> others) => others.Any(a => a.Status == CorporateActionStatus.Applied);

    /// <summary>The audit text of a suggestion declined because the other kind was applied on its day.</summary>
    public static string Describe(CorporateAction declined, string symbol) =>
        CorporateActionChecks.Describe(declined, symbol) + " declined: the other kind was applied on the same day";

    public static Error Conflict => DataErrors.CorporateActionConflictsSameDay;
}
