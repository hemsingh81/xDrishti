using System.Globalization;
using XDrishti.Domain.Market;

namespace XDrishti.Execution.Domain.Readiness;

public sealed record TokenCoverResult(ReadinessOutcome Outcome, string Detail, string Text);

/// <summary>
/// Spec P12-11 criterion 3: a token must outlive the session and the final reconciliation (default 16:45 India time of the session date), not just the open, and leave at least the minimum from now (the number P12-03 uses). Order of the judgement: no expiry is
/// unavailable; an expiry already past is <c>token_expired</c>; less than the minimum left is <c>token_expiring</c>; an expiry before the cut-off is <c>token_short</c>. An expiry exactly at the cut-off passes, one second earlier fails.
/// </summary>
public static class TokenCover
{
    public static DateTimeOffset CutOff(DateOnly sessionDate, TimeOnly coverUntil) => new(sessionDate.ToDateTime(coverUntil), IndiaTime.Offset);

    /// <summary>True when a token that ends at <paramref name="expiresAt"/> lasts to the cut-off of <paramref name="sessionDate"/>.</summary>
    public static bool Covers(DateTimeOffset expiresAt, DateOnly sessionDate, TimeOnly coverUntil) => expiresAt >= CutOff(sessionDate, coverUntil);

    public static TokenCoverResult Evaluate(DateTimeOffset? expiresAt, DateTimeOffset now, TimeOnly coverUntil, int minRemainingMinutes)
    {
        if (expiresAt is not { } expiry)
        {
            return new(ReadinessOutcome.Unavailable, "token_unavailable", "The connection has no known token end.");
        }

        var session = IndiaTime.Today(now);
        if (expiry <= now)
        {
            return new(ReadinessOutcome.Fail, "token_expired", "The token has ended: connect Dhan again.");
        }

        if (expiry - now < TimeSpan.FromMinutes(minRemainingMinutes))
        {
            return new(ReadinessOutcome.Fail, "token_expiring", $"The token ends in under {minRemainingMinutes} minutes: connect Dhan again.");
        }

        if (!Covers(expiry, session, coverUntil))
        {
            var ist = IndiaTime.ToIst(expiry);
            var when = DateOnly.FromDateTime(ist.DateTime) == session ? "today" : ist.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new(ReadinessOutcome.Fail, "token_short", $"The token expires at {ist:HH:mm} {when}: log in again between {coverUntil:HH:mm} yesterday and 09:00 today.");
        }

        return new(ReadinessOutcome.Pass, "token_covers", $"The token lasts past {coverUntil:HH:mm}.");
    }
}
