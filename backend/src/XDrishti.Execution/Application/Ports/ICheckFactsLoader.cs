using XDrishti.Execution.Domain.Checks;

namespace XDrishti.Execution.Application.Ports;

/// <summary>Everything one run judges. <c>Limits</c> null means the settings in force cannot be vouched for.</summary>
public sealed record CheckFacts(IntentFacts Intent, AccountFacts Account, MarketFacts Market, SystemFacts System, ExecLimits? Limits);

/// <summary>
/// Gathers the facts for an entry (read-only: quote, funds, margin, positions, profile, calendar, plan, halts, token). Returns null when it cannot answer at all. The default answers null: no source is wired yet, which rejects (P12-03, ADR 0159).
/// </summary>
public interface ICheckFactsLoader
{
    Task<CheckFacts?> LoadAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken);
}

internal sealed class ClosedCheckFactsLoader : ICheckFactsLoader
{
    public Task<CheckFacts?> LoadAsync(Guid accountId, long intentId, BrokerLeg leg, CancellationToken cancellationToken) => Task.FromResult<CheckFacts?>(null);
}
