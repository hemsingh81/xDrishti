namespace XDrishti.Execution.Domain.Protection;

/// <summary>A stop leg at the broker as last read: remaining quantity, status and trigger. TRANSIT and every other status do not protect; only PENDING does. The target and entry legs of a super order are never given here.</summary>
public sealed record StopLegView(string Key, int RemainingQty, string Status, decimal Trigger);

public abstract record ProtectionVerdict
{
    /// <summary>Nothing to protect (net quantity 0).</summary>
    public sealed record Flat : ProtectionVerdict;

    public sealed record Protected : ProtectionVerdict;

    /// <summary>Unprotected but inside the deadline.</summary>
    public sealed record Pending(DateTimeOffset DeadlineAt, int Uncovered) : ProtectionVerdict;

    /// <summary>Past the deadline, or a stop is looser than the intent's (<c>stop_mismatch</c>), or the net is negative (<c>oversold</c>).</summary>
    public sealed record Breach(string Reason, int Uncovered) : ProtectionVerdict;

    /// <summary>The broker's view is missing or too old: nothing can be said, and unavailable never counts as protected.</summary>
    public sealed record Unknown(string Reason) : ProtectionVerdict;
}

public sealed record ProtectionInput(int NetQty, IReadOnlyList<StopLegView> Legs, decimal IntentStop, DateTimeOffset? UnprotectedSince, DateTimeOffset Now, DateTimeOffset? BookReadAt, TimeSpan Deadline, TimeSpan BlindAfter);

/// <summary>The protection invariant as a pure verdict (spec P12-04 I1, I2). Judged from the broker's book as last read, never from what we intended.</summary>
public static class ProtectionMonitor
{
    public const string StatusPending = "PENDING";

    public static ProtectionVerdict Judge(ProtectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.NetQty < 0)
        {
            return new ProtectionVerdict.Breach("oversold", 0);
        }

        if (input.NetQty == 0)
        {
            return new ProtectionVerdict.Flat();
        }

        if (input.BookReadAt is not { } readAt || (input.Now - readAt) > input.BlindAfter)
        {
            return new ProtectionVerdict.Unknown(input.BookReadAt is null ? "no_book" : "blind");
        }

        var live = input.Legs.Where(l => string.Equals(l.Status, StatusPending, StringComparison.Ordinal) && l.RemainingQty > 0).ToList();
        if (live.Any(l => l.Trigger < input.IntentStop))
        {
            return new ProtectionVerdict.Breach("stop_mismatch", Math.Max(0, input.NetQty - live.Where(l => l.Trigger >= input.IntentStop).Sum(l => l.RemainingQty)));
        }

        var uncovered = input.NetQty - live.Sum(l => l.RemainingQty);
        if (uncovered <= 0)
        {
            return new ProtectionVerdict.Protected();
        }

        var deadlineAt = (input.UnprotectedSince ?? input.Now) + input.Deadline;
        return input.Now > deadlineAt ? new ProtectionVerdict.Breach("protection_breach", uncovered) : new ProtectionVerdict.Pending(deadlineAt, uncovered);
    }
}
