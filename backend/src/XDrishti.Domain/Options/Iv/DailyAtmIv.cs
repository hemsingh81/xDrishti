namespace XDrishti.Domain.Options.Iv;

/// <summary>
/// The daily ATM IV series of one underlying (P14-11, ADR 0199, pure): one value per session, the mean of the CE and PE offset-0 IV of the last bar of the session, as a fraction (the vendor value read as percent and divided by 100, <see cref="IvLabels.Unit"/>), on the front series of that date. A session with only one side, with an IV that is
/// not a positive finite number, or whose key-session is unusable has no value (never a half value and never zero).
/// </summary>
public static class DailyAtmIv
{
    /// <summary>The mean of a CE and a PE vendor IV as a fraction; null when either is missing or invalid.</summary>
    public static double? Mean(double? ceVendorIv, double? peVendorIv)
    {
        if (ceVendorIv is not { } ce || peVendorIv is not { } pe)
        {
            return null;
        }

        return Valid(ce) && Valid(pe) ? ((ce / 100.0) + (pe / 100.0)) / 2.0 : null;
    }

    // Not a finite number above zero, or above 500 percent, is no value (never 0): the pricing core's rule for a vendor vol.
    private static bool Valid(double percent) => double.IsFinite(percent) && percent > 0 && percent <= IvLabels.MaxPercent;

    /// <summary>The series: for each session the row of the code <paramref name="frontCode"/> gives (null for none), CE and PE both usable and valid.</summary>
    public static IReadOnlyDictionary<DateOnly, double> Build(IEnumerable<DailySideIv> rows, Func<DateOnly, int?> frontCode)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(frontCode);
        var index = new Dictionary<(DateOnly, int, string), DailySideIv>();
        foreach (var row in rows)
        {
            index[(row.Date, row.Code, row.Side)] = row;
        }

        var series = new SortedDictionary<DateOnly, double>();
        foreach (var date in index.Keys.Select(k => k.Item1).Distinct())
        {
            if (frontCode(date) is not { } code
                || !index.TryGetValue((date, code, "CE"), out var ce) || !index.TryGetValue((date, code, "PE"), out var pe)
                || !ce.Usable || !pe.Usable)
            {
                continue;
            }

            if (Mean(ce.VendorIv, pe.VendorIv) is { } value)
            {
                series[date] = value;
            }
        }

        return series;
    }
}
