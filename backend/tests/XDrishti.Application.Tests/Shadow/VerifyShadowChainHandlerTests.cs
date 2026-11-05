using XDrishti.Application.Shadow;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-08 criterion 4, the application half: the verb's handler recomputes the chain from what the store holds and names the first broken session.</summary>
public sealed class VerifyShadowChainHandlerTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ShadowEntry Entry(DateOnly session, string symbol) =>
        new(session, 1, symbol.Length, $"ORB|{symbol}|Buy|{session:yyyy-MM-dd}", "primary", ShadowFixture.Intraday, ShadowFixture.Hash, symbol, "BUY", 100m, 98m, 104m, "09:30-11:30", null, null, null, null, null, true, 1);

    private static ShadowFixture.MemoryStore Store()
    {
        var store = new ShadowFixture.MemoryStore();
        var one = new[] { Entry(Monday, "A"), Entry(Monday, "B") };
        var d1 = ShadowDigest.Next(null, Monday, "ok", one.Select(e => e.RowHash));
        store.WriteSessionAsync(new XDrishti.Application.Abstractions.Shadow.NewShadowSession(Monday, "ok", null, d1, one), Ct).AsTask().GetAwaiter().GetResult();
        var d2 = ShadowDigest.Next(d1, Monday.AddDays(1), "gap", []);
        store.WriteSessionAsync(new XDrishti.Application.Abstractions.Shadow.NewShadowSession(Monday.AddDays(1), "gap", d1, d2, []), Ct).AsTask().GetAwaiter().GetResult();
        return store;
    }

    [Fact]
    public async Task An_intact_chain_reports_its_size_and_the_last_digest_to_keep()
    {
        var view = await new VerifyShadowChainHandler(Store()).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.Sessions, view.Rows, view.FirstBroken, view.LastSession).ShouldBe((true, 2, 2, null, Monday.AddDays(1)));
        view.LastDigest.ShouldBe(ShadowDigest.Next(ShadowDigest.Next(null, Monday, "ok", new[] { Entry(Monday, "A"), Entry(Monday, "B") }.Select(e => e.RowHash)), Monday.AddDays(1), "gap", []));
    }

    [Fact]
    public async Task A_stored_digest_that_does_not_recompute_names_its_session()
    {
        var store = Store();
        var gap = store.Sessions[1];
        store.Sessions[1] = gap with { Digest = new string('0', 64) };

        var view = await new VerifyShadowChainHandler(store).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.FirstBroken).ShouldBe((false, Monday.AddDays(1)));
    }

    [Fact]
    public async Task An_empty_record_is_intact_with_nothing_to_keep()
    {
        var view = await new VerifyShadowChainHandler(new ShadowFixture.MemoryStore()).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.Sessions, view.LastDigest).ShouldBe((true, 0, null));
    }
}
