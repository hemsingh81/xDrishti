using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Application.Shadow;
using XDrishti.Domain.Platform.Services;
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

    private readonly IShadowDigestSink _sink = Substitute.For<IShadowDigestSink>();

    private readonly IServiceRunStore _runs = Substitute.For<IServiceRunStore>();

    private VerifyShadowChainHandler Handler(IShadowStore store)
    {
        _sink.ReadAsync(Ct).Returns(new ShadowDigestLogRead(false, [], null));
        _runs.ListAsync(ShadowDigestService.Id, 0, 100, Ct).Returns([]);
        return new VerifyShadowChainHandler(store, _sink, _runs);
    }

    private static ShadowDigestLine Logged(DateOnly session, string digest) => new("2026-W45", session, digest, 1, 0, DateTimeOffset.UnixEpoch);

    private async Task<ShadowChainCheckView> ReadWithLogAsync(ShadowFixture.MemoryStore store, params ShadowDigestLine[] lines)
    {
        var handler = Handler(store);
        _sink.ReadAsync(Ct).Returns(new ShadowDigestLogRead(true, lines, null));
        return await handler.HandleAsync(new VerifyShadowChainQuery(), Ct);
    }

    [Fact]
    public async Task A_log_that_agrees_with_the_chain_is_clean()
    {
        var store = Store();

        var view = await ReadWithLogAsync(store, Logged(Monday, store.Sessions[0].Digest), Logged(Monday.AddDays(1), store.Sessions[1].Digest));

        view.LogProblem.ShouldBeNull();
    }

    [Fact]
    public async Task A_recomputed_chain_with_a_different_digest_for_a_logged_session_is_found_although_the_chain_itself_verifies()
    {
        var store = Store();
        var logged = store.Sessions[1].Digest;
        var forged = ShadowDigest.Next(null, Monday, "gap", []); // the database owner rewrites session 1 as a gap and recomputes every later digest
        store.Sessions[0] = store.Sessions[0] with { Status = "gap", Entries = [], Digest = forged };
        store.Sessions[1] = store.Sessions[1] with { PrevDigest = forged, Digest = ShadowDigest.Next(forged, Monday.AddDays(1), "gap", []) };

        var view = await ReadWithLogAsync(store, Logged(Monday.AddDays(1), logged));

        view.Intact.ShouldBeTrue();
        view.LogProblem.ShouldNotBeNull().ShouldContain("differs");
    }

    [Fact]
    public async Task A_different_digest_for_an_older_logged_session_is_found_too()
    {
        var store = Store();

        var view = await ReadWithLogAsync(store, Logged(Monday, new string('0', 64)), Logged(Monday.AddDays(1), store.Sessions[1].Digest));

        view.LogProblem.ShouldNotBeNull().ShouldContain(Monday.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task A_logged_session_the_chain_does_not_hold_is_found()
    {
        var view = await ReadWithLogAsync(Store(), Logged(Monday.AddDays(9), new string('a', 64)));

        view.LogProblem.ShouldNotBeNull().ShouldContain("does not hold");
    }

    [Fact]
    public async Task A_missing_log_is_a_problem_only_when_the_service_exported_before()
    {
        var store = Store();
        var handler = Handler(store);
        (await handler.HandleAsync(new VerifyShadowChainQuery(), Ct)).LogProblem.ShouldBeNull();
        var run = ServiceRun.Start(ShadowDigestService.Id, ServiceRunTrigger.Scheduled, 1, DateTimeOffset.UnixEpoch);
        run.Succeed(DateTimeOffset.UnixEpoch, ShadowDigestExported.ExportedPrefix + "2026-11-02 appended to the log outside the database.");
        _runs.ListAsync(ShadowDigestService.Id, 0, 100, Ct).Returns([run]);

        (await handler.HandleAsync(new VerifyShadowChainQuery(), Ct)).LogProblem.ShouldNotBeNull().ShouldContain("deleted or emptied");
    }

    [Fact]
    public async Task An_untrustworthy_log_is_reported_as_it_is()
    {
        var handler = Handler(Store());
        _sink.ReadAsync(Ct).Returns(new ShadowDigestLogRead(true, [], "Line 2 of the digest log is malformed."));

        (await handler.HandleAsync(new VerifyShadowChainQuery(), Ct)).LogProblem.ShouldBe("Line 2 of the digest log is malformed.");
    }

    [Fact]
    public async Task An_intact_chain_reports_its_size_and_the_last_digest_to_keep()
    {
        var view = await Handler(Store()).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.Sessions, view.Rows, view.FirstBroken, view.LastSession).ShouldBe((true, 2, 2, null, Monday.AddDays(1)));
        view.LastDigest.ShouldBe(ShadowDigest.Next(ShadowDigest.Next(null, Monday, "ok", new[] { Entry(Monday, "A"), Entry(Monday, "B") }.Select(e => e.RowHash)), Monday.AddDays(1), "gap", []));
    }

    [Fact]
    public async Task A_stored_digest_that_does_not_recompute_names_its_session()
    {
        var store = Store();
        var gap = store.Sessions[1];
        store.Sessions[1] = gap with { Digest = new string('0', 64) };

        var view = await Handler(store).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.FirstBroken).ShouldBe((false, Monday.AddDays(1)));
    }

    [Fact]
    public async Task An_empty_record_is_intact_with_nothing_to_keep()
    {
        var view = await Handler(new ShadowFixture.MemoryStore()).HandleAsync(new VerifyShadowChainQuery(), Ct);

        (view.Intact, view.Sessions, view.LastDigest).ShouldBe((true, 0, null));
    }
}
