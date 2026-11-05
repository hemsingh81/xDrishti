using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>Plan items and an in-memory shadow store for the shadow writer's tests.</summary>
internal static class ShadowFixture
{
    public const string Intraday = "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*";

    public const string Daily = "PEAD|1D|BUY|swing|FIXED_RR{rr=2}|*";

    public static readonly string Hash = new('a', 64);

    public static readonly Guid Account = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public static NewPlanItem Item(int rank, TicketGroup group, string symbol, string strategy = "ORB", string cell = Intraday, bool reachedFill = true, string? note = null, TicketLevels? levels = null) =>
        new(
            rank, group, symbol, "BUY", strategy, Hash, "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", group == TicketGroup.Primary ? [] : ["why"],
            new PlanEvidence(cell, 41, "replayed history", "cell statistics only", 0.72, 0.12, reachedFill ? [] : ["portfolio_fill", "checklist", "smart_rules", "allocation"], note, 0.7),
            group == TicketGroup.Primary ? [new NewAllocation(Account, "paper", 100)] : [],
            levels ?? TicketLevels.Create("BUY", 100m, new DateOnly(2026, 11, 2), 98m, StopAnchor.CloseOfD, 104m, 2m).Value);

    public static PlanRow Plan(long id, DateOnly forDate, int version = 1, PlanState state = PlanState.Published) =>
        new(id, new NewPlan("key-" + id, forDate, ShadowCutoffFor(forDate), version, new string('c', 64), "basis", true, "test", DateTimeOffset.UnixEpoch, []), state, 0, 0, 0);

    public static PlanItemRow Row(long id, long planId, NewPlanItem item) => new(id, planId, item, TicketState.Proposed);

    private static DateTimeOffset ShadowCutoffFor(DateOnly session) => new(session.ToDateTime(new TimeOnly(9, 5)), TimeSpan.FromMinutes(330));

    /// <summary>The store as the database behaves: a session of a stored date is refused, and what was written is kept for the test to read.</summary>
    internal sealed class MemoryStore : IShadowStore
    {
        public List<NewShadowSession> Sessions { get; } = [];

        public int WriteCalls { get; private set; }

        public ValueTask<ShadowHead?> HeadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Sessions.OrderByDescending(s => s.SessionDate).FirstOrDefault() is { } s ? new ShadowHead(s.SessionDate, s.Digest) : null);

        public ValueTask<bool> WriteSessionAsync(NewShadowSession session, CancellationToken cancellationToken)
        {
            WriteCalls++;
            if (Sessions.Any(s => s.SessionDate == session.SessionDate))
            {
                return ValueTask.FromResult(false);
            }

            Sessions.Add(session);
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<ShadowChainSession>> ReadChainAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ShadowChainSession>>([.. Sessions.OrderBy(s => s.SessionDate).Select(s => new ShadowChainSession(s.SessionDate, s.Status, s.PrevDigest, s.Digest, [.. s.Entries.Select(e => new StoredShadowEntry(e, e.RowHash))]))]);
    }
}
