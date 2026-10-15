using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>Made-up facts for the attention items: a test adds accounts, health rows and so on, and the clock gives the time.</summary>
internal sealed class StaticAttentionFacts(TimeProvider clock) : IAttentionFactsSource
{
    public List<AttentionAccount> Accounts { get; } = [];

    public Dictionary<(Guid Account, SyncHealthKind Kind), SyncHealth> Health { get; } = [];

    public Dictionary<Guid, BrokerSyncCounts> TradeCounts { get; } = [];

    public Dictionary<Guid, HoldingFlags> Flags { get; } = [];

    public Dictionary<Guid, PendingDividends> Dividends { get; } = [];

    public Dictionary<Guid, UnmatchedCounts> Unmatched { get; } = [];

    public Guid? DataAccountId { get; set; }

    public string? DataAccountName { get; set; }

    public int Loads { get; private set; }

    public ValueTask<AttentionFacts> LoadAsync(CancellationToken cancellationToken)
    {
        Loads++;
        return ValueTask.FromResult(new AttentionFacts(clock.GetUtcNow(), [.. Accounts], Health, TradeCounts, Flags, Dividends, Unmatched, DataAccountId, DataAccountName));
    }

    /// <summary>An account that is connected for a good while, with the Portfolio role on.</summary>
    public AttentionAccount Add(string name, bool portfolio = true, ConnectionStatus status = ConnectionStatus.Connected, DateTimeOffset? expiresAt = null, DateTimeOffset? lastConnectedAt = null)
    {
        var account = new AttentionAccount(Guid.NewGuid(), name, portfolio, status, expiresAt ?? clock.GetUtcNow().AddHours(10), lastConnectedAt ?? clock.GetUtcNow().AddHours(-2));
        Accounts.Add(account);
        return account;
    }
}
