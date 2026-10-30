namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// The only way a halt ends without the owner (P12-05). It is fed one observation per tick for a halt: <c>true</c> good state, <c>false</c> bad, <c>null</c> unavailable. Unavailable and bad both restart the count, so an unreadable source can never clear
/// a halt. A clear is written only when the trigger's table row has an automatic rule and the rule is met; owner-only triggers are never cleared here whatever is observed.
/// </summary>
public sealed class HaltAutoClearService(IHaltStore store, TimeProvider clock)
{
    private readonly Dictionary<long, (DateTimeOffset Since, int Reads)> _good = [];

    // For ReadyFact codes the caller passes true only when the readiness fact (P12-11) says ready; there is no such fact yet, so nothing clears them.
    public async Task<bool> ObserveAsync(ActiveHalt halt, bool? good, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(halt);
        if (HaltTriggers.Find(halt.TriggerCode)?.AutoClear is not { } rule)
        {
            return false;
        }

        var now = clock.GetUtcNow();
        if (good is not true)
        {
            _good.Remove(halt.Id);
            return false;
        }

        var (since, reads) = _good.TryGetValue(halt.Id, out var seen) ? seen : (now, 0);
        _good[halt.Id] = (since, ++reads);
        var met = rule.Kind switch
        {
            AutoClearKind.QuietSeconds => now - since >= TimeSpan.FromSeconds(rule.Amount),
            AutoClearKind.GoodReads => reads >= rule.Amount,
            _ => true, // ReadyFact: the caller passes true only when the fact says ready
        };
        if (!met)
        {
            return false;
        }

        _good.Remove(halt.Id);
        return await store.AutoClearAsync(halt.Id, halt.TriggerCode, $"{rule.Kind}:{rule.Amount}", now, cancellationToken).ConfigureAwait(false);
    }
}
