using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The exact words of a confirm ticket (spec P12-08 criterion 2, D2): a pure function of the stored parameters, rendered by the server once, at issue, and never by the browser. Every parameter appears in the text at a fixed place with an
/// injective format (prices with two to four decimals, the quantity in full, times in India time to the minute and the second the parameters hold), so a changed parameter changes the text and so its hash. Money is grouped the Indian way.
/// Culture-free: the same parameters give the same text on every machine.
/// </summary>
public static class ConfirmText
{
    /// <summary>The longest text a ticket may hold; a longer one is not issued.</summary>
    public const int MaxLength = 2000;

    public static string Render(ConfirmTicketParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var approved = IndiaTime.ToIst(p.ApprovedAt);
        var expires = IndiaTime.ToIst(p.ExpiresAt);
        var canary = p.Canary ? ", canary: 1 share" : string.Empty;
        var text = new StringBuilder(400)
            .Append(p.Side).Append(' ').Append(Whole(p.Quantity)).Append(' ').Append(p.Symbol)
            .Append(" at LIMIT Rs ").Append(Price(p.Limit)).Append(" (NSE, intraday").Append(canary).Append("), valid today. ")
            .Append("Stop-loss Rs ").Append(Price(p.Stop)).Append(" and target Rs ").Append(Price(p.Target)).Append(" are placed at the broker with the order (one super order). ")
            .Append("Notional Rs ").Append(Money(p.Notional)).Append(". ")
            .Append("Worst case at the stop Rs ").Append(Money(p.RiskAtStop)).Append(" (").Append(Whole(p.Quantity)).Append(" x ").Append(Price(p.StopDistance)).Append("). ")
            .Append("Account: ").Append(p.Broker).Append(" · ").Append(p.AccountName).Append(". ")
            .Append("Plan ticket approved by you on ").Append(approved.ToString("d MMM", CultureInfo.InvariantCulture)).Append(" at ").Append(approved.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(". ")
            .Append("This card expires at ").Append(expires.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append('.');
        return text.ToString();
    }

    /// <summary>SHA-256 of the UTF-8 text, 64 lower-case hex digits: what the browser echoes back with the tap (the database checks the same hash on insert).</summary>
    public static string Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Two decimals at least, four at most (the record's scale), so 412.35 and 412.3525 read differently.
    private static string Price(decimal value) => value.ToString("0.00##", CultureInfo.InvariantCulture);

    // Indian grouping of rupees and paise: 1,23,456.78.
    private static string Money(decimal value)
    {
        var text = decimal.Round(Math.Abs(value), 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var whole = text[..point];
        var grouped = new StringBuilder();
        var head = whole.Length > 3 ? whole[..^3] : string.Empty;
        for (var i = 0; i < head.Length; i++)
        {
            if (i > 0 && (head.Length - i) % 2 == 0)
            {
                grouped.Append(',');
            }

            grouped.Append(head[i]);
        }

        if (head.Length > 0)
        {
            grouped.Append(',');
        }

        grouped.Append(whole.Length > 3 ? whole[^3..] : whole).Append(text[point..]);
        return (value < 0 ? "-" : string.Empty) + grouped;
    }
}
