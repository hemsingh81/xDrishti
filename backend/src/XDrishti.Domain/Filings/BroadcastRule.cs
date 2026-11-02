using System.Globalization;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The broadcast time rule (P15-01, D3): the time is the exchange's dissemination time as the owner typed it with an offset. It is never taken from a file's modified time, a name, the sidecar's creation time or the ingest time, and a date without a time is never
/// turned into a time. Reasons (closed): <c>missing</c>, <c>date_only</c>, <c>no_offset</c>, <c>unparsable</c>, <c>before_2000</c>, <c>broadcast_in_future</c>.
/// </summary>
public static partial class BroadcastRule
{
    public const string Missing = "missing";
    public const string DateOnly = "date_only";
    public const string NoOffset = "no_offset";
    public const string Unparsable = "unparsable";
    public const string Before2000 = "before_2000";
    public const string InFuture = "broadcast_in_future";

    private static readonly DateTimeOffset Floor = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateOnlyPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NoOffsetPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex WithOffsetPattern();

    public static BroadcastTime Evaluate(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unavailable(Missing);
        }

        var text = raw.Trim();
        if (DateOnlyPattern().IsMatch(text))
        {
            return Unavailable(DateOnly);
        }

        if (NoOffsetPattern().IsMatch(text))
        {
            return Unavailable(NoOffset);
        }

        if (!WithOffsetPattern().IsMatch(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return Unavailable(Unparsable);
        }

        var instant = parsed.ToUniversalTime();
        if (instant < Floor)
        {
            return Unavailable(Before2000);
        }

        return instant > now.ToUniversalTime().AddMinutes(5) ? Unavailable(InFuture) : new BroadcastTime(instant, BroadcastState.Ok, null);
    }

    private static BroadcastTime Unavailable(string reason) => new(null, BroadcastState.Unavailable, reason);
}
