using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Application.Ports;

/// <summary>The head of a stored run (enough to decide whether it is still valid).</summary>
public sealed record StoredRun(long Id, CheckRunKind Kind, DateTimeOffset At, DateTimeOffset ValidUntil, CheckVerdict Verdict);

/// <summary>Insert-only record of the runs, their 34 (or 7) results and the owner's confirmations. A store that cannot write makes the check a refusal.</summary>
public interface ICheckStore
{
    Task<long> AppendRunAsync(long intentId, CheckRun run, CancellationToken cancellationToken);

    Task<StoredRun?> LatestFullRunAsync(long intentId, CancellationToken cancellationToken);

    Task AppendConfirmationAsync(long runId, CheckConfirmation confirmation, CancellationToken cancellationToken);

    Task<CheckConfirmation?> LatestConfirmationAsync(long intentId, CancellationToken cancellationToken);
}
