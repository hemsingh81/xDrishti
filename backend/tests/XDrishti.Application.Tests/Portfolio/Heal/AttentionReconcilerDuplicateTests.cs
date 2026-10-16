using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>A derived id that repeats must not reach the store: one row per id is written in one statement, and a repeat would fail it for every later reconcile too.</summary>
public sealed class AttentionReconcilerDuplicateTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 4, 30, 0, TimeSpan.Zero));
    private readonly CapturingStore _store = new();
    private readonly CapturingLogger _logger = new();

    private async Task ReconcileAsync(StaticAttentionFacts facts) =>
        await new AttentionReconciler(facts, _store, new FixedHealSettings(), _clock, _logger).ReconcileAsync(Ct);

    [Fact]
    public async Task A_repeated_account_gives_one_draft_per_id_and_a_warning()
    {
        var facts = new StaticAttentionFacts(_clock);
        var account = facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _clock.GetUtcNow().AddHours(-1));
        facts.Accounts.Add(account); // the same account listed twice derives the same item twice

        await ReconcileAsync(facts);

        _store.Drafts.Count.ShouldBe(1);
        _store.Drafts.Select(d => d.Id).Distinct().Count().ShouldBe(_store.Drafts.Count);
        _logger.Warnings.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_repeat_every_draft_goes_through_and_nothing_is_logged()
    {
        var facts = new StaticAttentionFacts(_clock);
        facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _clock.GetUtcNow().AddHours(-1));
        facts.Add("Example Two", status: ConnectionStatus.Expired, expiresAt: _clock.GetUtcNow().AddHours(-1));

        await ReconcileAsync(facts);

        _store.Drafts.Count.ShouldBe(2);
        _logger.Warnings.ShouldBe(0);
    }

    [Fact]
    public async Task A_store_that_refuses_a_repeated_id_is_not_reached_with_one()
    {
        var facts = new StaticAttentionFacts(_clock);
        var account = facts.Add("Example One", status: ConnectionStatus.Expired, expiresAt: _clock.GetUtcNow().AddHours(-1));
        facts.Accounts.Add(account);
        _store.RefuseRepeats = true;

        await ReconcileAsync(facts);

        _store.Writes.ShouldBe(1);
        _logger.Failures.ShouldBe(0);
    }

    private sealed class CapturingStore : IAttentionStore
    {
        public List<AttentionDraft> Drafts { get; private set; } = [];

        public int Writes { get; private set; }

        public bool RefuseRepeats { get; set; }

        public ValueTask ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (RefuseRepeats && current.Select(c => c.Id).Distinct().Count() != current.Count)
            {
                throw new InvalidOperationException("ON CONFLICT DO UPDATE command cannot affect row a second time");
            }

            Writes++;
            Drafts = [.. current];
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<StoredAttention>> ListAsync(DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredAttention>>([]);

        public ValueTask<int> DeleteResolvedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => ValueTask.FromResult(0);
    }

    private sealed class CapturingLogger : ILogger<AttentionReconciler>
    {
        public int Warnings { get; private set; }

        public int Failures { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
            }

            if (exception is not null)
            {
                Failures++;
            }
        }
    }
}
