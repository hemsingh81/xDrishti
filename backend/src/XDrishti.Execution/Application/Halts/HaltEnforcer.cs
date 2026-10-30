using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Protection;

namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// Carries out what a halt level asks beyond blocking entries (P12-05 criteria 3 and 4). At <c>cancel</c> and above every working entry of the halt's scope is cancelled, at most three attempts one second apart, each judged by the broker's book, and a cancel still
/// not visible after the third is a CRITICAL <c>exit_failed</c> alarm; protective legs are never touched. At <c>exit_all</c> the positions are flattened by <see cref="SquareOffService"/> held at its square-off step (stops cancelled only once flat). At
/// <c>freeze</c> it does nothing (no cancel, no tightening). Shortcuts: the three-attempt budget is in memory (a restart resets it) and a retry is another cancel judged by the book, not a lookup; upgrade: persist the tries and use LegLookup once the lookup path is verified. It acts through the protective guard like every other protective action, so a closed gate does nothing.
/// </summary>
public sealed class HaltEnforcer(IHaltStore halts, IProtectionSource source, IBrokerOrders broker, ProtectiveGuard guard, ProtectionReporter report, SquareOffService squareOff, TimeProvider clock)
{
    public const int CancelAttempts = 3;
    public static readonly TimeSpan CancelGap = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, (int Tries, DateTimeOffset At)> _cancels = [];

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ActiveHalt> active;
        try
        {
            active = await halts.ActiveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return; // an unreadable record blocks entries elsewhere; nothing is cancelled on a guess
        }

        if (active.Count == 0 || await source.ReadAsync(cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var entry in snapshot.Entries)
        {
            var level = HaltRules.Effective(active, entry.AccountId);
            if (level is null || level == HaltLevel.Entries || !HaltRules.Permits(level, HaltAction.CancelWorkingEntry))
            {
                continue;
            }

            await CancelAsync(entry, now, cancellationToken).ConfigureAwait(false);
        }

        if (active.Any(h => h.Level == HaltLevel.ExitAll))
        {
            await squareOff.ForceTickAsync(account => HaltRules.Effective(active, account) == HaltLevel.ExitAll, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CancelAsync(WorkingEntry entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (tries, at) = _cancels.TryGetValue(entry.ClientOrderId, out var seen) ? seen : (0, DateTimeOffset.MinValue);
        if (now - at < CancelGap)
        {
            return;
        }

        if (tries >= CancelAttempts)
        {
            await report.RaiseAsync(AlarmCodes.ExitFailed, entry.AccountId, entry.IntentId, "a working entry could not be cancelled under the halt after three attempts", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!(await guard.OpenAsync(entry.AccountId, cancellationToken).ConfigureAwait(false)).Open)
        {
            return;
        }

        _cancels[entry.ClientOrderId] = (tries + 1, now);
        await broker.CancelAsync(entry.ClientOrderId, cancellationToken).ConfigureAwait(false);
    }
}
