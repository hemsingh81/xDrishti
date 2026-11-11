namespace XDrishti.Domain.Options;

/// <summary>
/// One row of <c>options.lot_sizes</c>: the lot of an underlying's options from a date on. <see cref="Source"/> is <c>circular</c> (read in the exchange's
/// circular), <c>secondary</c> (a secondary source, unconfirmed), <c>scrip_master</c> (what Dhan's file showed on the day of a refresh) or <c>nse_mktlots</c> (the nearest-expiry lot of NSE's <c>fo_mktlots.csv</c> on the day it was fetched, BK-05b; never confirmed). <see cref="Id"/>
/// orders rows recorded later.
/// </summary>
public sealed record LotSizeEntry(long Id, Guid UnderlyingId, DateOnly EffectiveFrom, int LotSize, string Source, bool Confirmed)
{
    public const string Circular = "circular";

    public const string Secondary = "secondary";

    public const string ScripMaster = "scrip_master";

    public const string NseMktLots = "nse_mktlots";
}
