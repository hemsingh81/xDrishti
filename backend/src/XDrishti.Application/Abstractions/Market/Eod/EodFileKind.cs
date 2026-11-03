namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary>The NSE end-of-day files this slice fetches (CF-04 Part A): the daily price file and the delivery file. The corporate-actions file is read from a local file only (it goes to the import preview).</summary>
public enum EodFileKind
{
    Bhavcopy,
    Delivery,
}
