using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Planning;

/// <summary>
/// One stored loosening of a limit (BK-10b): a row with direction loosen of the sizing settings, a basket budget or the basket settings. The row is read as it was stored; its direction is never recomputed here.
/// </summary>
/// <param name="Source"><see cref="LooseningNoticeId.Sizing"/>, <see cref="LooseningNoticeId.Budget"/> or <see cref="LooseningNoticeId.Basket"/>.</param>
/// <param name="Id">The id of the change row in its table.</param>
/// <param name="Key">The setting key, or for a budget the basket's name.</param>
/// <param name="BasketId">The basket of a budget; null otherwise.</param>
/// <param name="OldValue">The value before, as text (a budget that did not exist reads <c>off</c>).</param>
/// <param name="NewValue">The value after, as text (a removed budget reads <c>off</c>).</param>
/// <param name="Actor">Who changed it, as stored.</param>
/// <param name="ChangedAt">When it was stored.</param>
public sealed record LooseningNotice(string Source, long Id, string Key, Guid? BasketId, string OldValue, string NewValue, string Actor, DateTimeOffset ChangedAt)
{
    /// <summary>The notice id: <c>sizing:{id}</c>, <c>budget:{id}</c> or <c>basket:{id}</c>.</summary>
    public string NoticeId => LooseningNoticeId.Format(Source, Id);

    /// <summary>The sentence of the banner, text only.</summary>
    public string Sentence => string.Create(CultureInfo.InvariantCulture, $"Loosened since {IndiaTime.ToIst(ChangedAt):yyyy-MM-dd}: {Key} {OldValue} → {NewValue} (by {Actor}).");
}
