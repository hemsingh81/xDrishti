namespace XDrishti.Application.Market.Eod;

/// <summary>
/// NSE's archive host and the path patterns of the daily files (CF-04 Part A). <b>UNVERIFIED until the probe has run</b> (<c>xd data eod-probe</c>): these are the best-known patterns, to be verified against the raw files the probe stores.
/// They are the only place that names a path; the owner puts matching entries on the allow-list (ADR 0195), the app never does.
/// </summary>
public static class EodArchive
{
    public const string Host = "nsearchives.nseindia.com";

    /// <summary>First trading day of the post-July-2024 "UDiFF" bhavcopy (UNVERIFIED; the parser decides by header, this only picks the URL).</summary>
    public static readonly DateOnly UdiffFrom = new(2024, 7, 8);

    private static readonly string[] Months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    /// <summary>Path of the bhavcopy of a date on <see cref="Host"/> (UNVERIFIED).</summary>
    public static string BhavcopyPath(DateOnly date)
    {
        var month = Months[date.Month - 1];
        return date >= UdiffFrom
            ? $"/content/cm/BhavCopy_NSE_CM_0_0_0_{date:yyyyMMdd}_F_0000.csv.zip"
            : $"/content/historical/EQUITIES/{date:yyyy}/{month}/cm{date:dd}{month}{date:yyyy}bhav.csv.zip";
    }

    /// <summary>Path of the delivery file (MTO) of a date on <see cref="Host"/> (UNVERIFIED).</summary>
    public static string DeliveryPath(DateOnly date) => $"/archives/equities/mto/MTO_{date:ddMMyyyy}.DAT";
}
