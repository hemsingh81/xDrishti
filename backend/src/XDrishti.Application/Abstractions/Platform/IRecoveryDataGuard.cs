namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// Tells the services that read the stored bars whether the end-of-day data they rely on is proved complete (spec AU-03, ADR 0240): after a recovery step whose proof found sessions <c>still_missing</c> the nightly plan, the shadow
/// grading and the paper capture must not run on the hole (a grade over a missing minute would not be graded again). Reads only; it never changes the ledger.
/// </summary>
public interface IRecoveryDataGuard
{
    /// <summary>
    /// Why the data of sessions on or after <paramref name="from"/> (all recent sessions when null) is not proved, in words; null when it is. A newest end-of-day recovery item that is <c>partial</c> is proved again now (a later fetch may have
    /// filled the hole), so the answer is never stale; when that cannot be read the answer is a reason too (fail closed). Only <c>still_missing</c> sessions count: an unfillable one is permanent and is not waited for.
    /// </summary>
    ValueTask<string?> WhyUnprovenAsync(DateOnly? from, CancellationToken cancellationToken);
}
