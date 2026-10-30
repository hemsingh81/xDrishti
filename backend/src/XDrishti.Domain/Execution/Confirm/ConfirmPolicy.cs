namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// The pure rules of the one-tap confirm (spec P12-08 criteria 3 to 13). Every input is an argument; an unreadable fact is the refusing side. The database judges the same tap again (trigger <c>execution.check_ticket_outcome</c>), so the
/// two cannot disagree silently: the clock that decides expiry is the database's.
/// </summary>
public static class ConfirmPolicy
{
    /// <summary>A confirmed outcome hands the intent to the sender only while it is this young (D6); later the intent is cancelled <c>expired_after_confirm</c>.</summary>
    public static readonly TimeSpan ConfirmedFreshFor = TimeSpan.FromSeconds(30);

    /// <summary>The readiness loop stores a row at least every 15 minutes (ADR 0164): an older newest row means the loop is not running, which blocks like not ready.</summary>
    public static readonly TimeSpan ReadinessMaxAge = TimeSpan.FromMinutes(16);

    public const int MaxAttempts = 2;

    public const int MaxOpenPerAccount = 3;

    public const int MaxReasonLength = 200;

    public const string ExpiredAfterConfirm = "expired_after_confirm";

    /// <summary>The modes in which a confirmed ticket may lead to a send.</summary>
    public static readonly IReadOnlyList<string> ConfirmableModes = ["semi_auto", "auto_live"];

    /// <summary>At or after the expiry instant the tap is late: 09:47:29.9 passes, 09:47:30.000 does not.</summary>
    public static bool IsExpired(DateTimeOffset now, DateTimeOffset expiresAt) => now >= expiresAt;

    /// <summary>The first state refusal of the tap, in order: late, switch OFF, mode no longer confirmable, halted (a day halt or the kill switch), not ready, the day not unlocked. Null when none applies.</summary>
    public static string? StateRefusal(ConfirmState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (IsExpired(s.Now, s.ExpiresAt))
        {
            return ConfirmCodes.TicketExpired;
        }

        if (s.ExecutionEnabled is not true)
        {
            return ConfirmCodes.Disabled;
        }

        if (s.EffectiveMode is null || !ConfirmableModes.Contains(s.EffectiveMode))
        {
            return ConfirmCodes.ModeChanged;
        }

        if (s.Halted is not false)
        {
            return ConfirmCodes.Halted;
        }

        if (!IsReady(s.ReadinessVerdict, s.ReadinessSession, s.ReadinessAt, s.TicketSession, s.Now))
        {
            return ConfirmCodes.NotReady;
        }

        return s.Unlocked ? null : ConfirmCodes.NotUnlocked;
    }

    /// <summary>Ready only on a <c>ready</c> verdict of the ticket's session younger than <see cref="ReadinessMaxAge"/>.</summary>
    public static bool IsReady(string? verdict, DateOnly? checkSession, DateTimeOffset? checkAt, DateOnly session, DateTimeOffset now) =>
        verdict == "ready" && checkSession == session && checkAt is { } at && at <= now + TimeSpan.FromSeconds(1) && now - at <= ReadinessMaxAge;

    /// <summary>The move of the last price from the reference, in percent of the reference; a fall counts as a rise.</summary>
    public static decimal DriftPct(decimal refPrice, decimal last) => refPrice <= 0 ? decimal.MaxValue : Math.Abs(last - refPrice) / refPrice * 100m;

    /// <summary>
    /// The price refusal of the tap: no fresh last price is <see cref="ConfirmCodes.PriceUnavailable"/>; a drift above the limit is <see cref="ConfirmCodes.PriceMoved"/> (412.30 with 0.30 %: 413.53 passes at 0.298 %, 413.54 does not at
    /// 0.301 %). Null when the price is fresh and inside the limit.
    /// </summary>
    public static string? PriceRefusal(decimal refPrice, decimal? last, DateTimeOffset? lastAt, DateTimeOffset now, ConfirmSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (last is not > 0 || lastAt is not { } at || at > now + TimeSpan.FromSeconds(1) || now - at > settings.FeedMaxAge)
        {
            return ConfirmCodes.PriceUnavailable;
        }

        return DriftPct(refPrice, last.Value) > settings.MaxDriftPct ? ConfirmCodes.PriceMoved : null;
    }

    /// <summary>The refusals that close the ticket <c>refused</c> (the one re-issue may follow); the state refusals leave it open until it expires.</summary>
    public static bool ClosesTicket(string code) => code is ConfirmCodes.PriceMoved or ConfirmCodes.PriceUnavailable or ConfirmCodes.SoftSetChanged;

    /// <summary>A Soft confirmation is bound to the exact set of Soft codes not passed at issue: the same set now, in any order.</summary>
    public static bool SameSoftSet(IReadOnlyCollection<string> atIssue, IReadOnlyCollection<string>? now) =>
        now is not null && atIssue.ToHashSet(StringComparer.Ordinal).SetEquals(now);

    /// <summary>
    /// The password with the tap (criterion 9): when the notional is at least <see cref="ConfirmSettings.StepUpNotionalPct"/> of the account's capital (Rs 400,000 at 10 % is a threshold of Rs 40,000), when a Soft check did not pass, or when
    /// the capital is unknown.
    /// </summary>
    public static PasswordRule Password(decimal notional, decimal? capital, decimal stepUpPct, bool softNonPass)
    {
        List<string> reasons = [];
        if (capital is not > 0)
        {
            reasons.Add(PasswordRule.CapitalUnavailable);
        }
        else if (notional >= capital.Value * stepUpPct / 100m)
        {
            reasons.Add(PasswordRule.NotionalOverStepUp);
        }

        if (softNonPass)
        {
            reasons.Add(PasswordRule.SoftChecks);
        }

        return new PasswordRule(reasons.Count > 0, reasons);
    }

    /// <summary>The owner's free-text reason (a Soft confirmation or a skip): 1 to 200 characters of plain text after trimming, no control characters.</summary>
    public static bool ReasonOk(string? reason) => reason?.Trim() is { Length: > 0 and <= MaxReasonLength } text && !text.Any(char.IsControl);
}
