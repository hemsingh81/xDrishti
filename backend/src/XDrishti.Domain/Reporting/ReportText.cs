using System.Globalization;

namespace XDrishti.Domain.Reporting;

/// <summary>The number formats of the report sentences: invariant culture, so the same figures give the same bytes on any machine.</summary>
internal static class ReportText
{
    /// <summary>R with a sign and two decimals, for example <c>+0.42 R</c>.</summary>
    public static string R(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " R";

    /// <summary>A rate as a whole percent.</summary>
    public static string Percent(double rate) => (rate * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>A count.</summary>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A plain two-decimal figure.</summary>
    public static string Plain(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
