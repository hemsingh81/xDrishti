using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Live;
using static XDrishti.Application.Tests.Live.FeedTestData;

namespace XDrishti.Application.Tests.Live;

/// <summary>Spec P7-11: armed tickets of today's plan join the feed's set and leave it; the cap refuses the armed ticket and displaces nobody; with none armed the set is exactly what it was.</summary>
public sealed class ArmedTicketsProviderTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 26, 3, 45, 0, TimeSpan.Zero);

    private static readonly Guid Armed1 = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    private static readonly Guid Armed2 = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private readonly IArmedTicketReader _armed = Substitute.For<IArmedTicketReader>();

    private readonly IInstrumentSecurities _securities = Substitute.For<IInstrumentSecurities>();

    private readonly FakeTimeProvider _time = new(Morning);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ArmedTicketsProvider Provider() => new(_armed, _securities, _time, NullLogger<ArmedTicketsProvider>.Instance);

    private void Armed(params ArmedTicket[] tickets) =>
        _armed.ListAsync(new DateOnly(2026, 10, 26), Ct).Returns(tickets);

    private void Known(params InstrumentSecurity[] found) =>
        _securities.FindAsync(Arg.Any<IReadOnlyCollection<string>>(), Ct).Returns(found.ToDictionary(f => f.Symbol, StringComparer.Ordinal));

    [Fact]
    public async Task An_armed_ticket_of_today_is_proposed_in_quote_mode_with_the_armed_reason()
    {
        Armed(new ArmedTicket(5, 1, 1, "NSE:AAA"));
        Known(new InstrumentSecurity("NSE:AAA", Armed1, "777"));

        var candidates = await Provider().ListAsync(Ct);

        var candidate = candidates.ShouldHaveSingleItem();
        candidate.InstrumentId.ShouldBe(Armed1);
        candidate.SecurityId.ShouldBe("777");
        candidate.Mode.ShouldBe(FeedMode.Quote);
        candidate.Reason.ShouldBe(ActiveSetReason.ArmedTicket);
    }

    [Fact]
    public async Task It_reads_the_plan_of_the_india_date_and_leaves_with_the_ticket_state()
    {
        Armed(new ArmedTicket(5, 1, 1, "NSE:AAA"));
        Known(new InstrumentSecurity("NSE:AAA", Armed1, "777"));
        var provider = Provider();
        var before = ActiveSetBuilder.Build([Holding(Reliance, "2885", 100m), .. await provider.ListAsync(Ct)], 200);

        // The expiry job moved the ticket to Expired: the reader no longer lists it.
        Armed();
        var after = ActiveSetBuilder.Build([Holding(Reliance, "2885", 100m), .. await provider.ListAsync(Ct)], 200);

        before.Members.Select(m => m.InstrumentId).ShouldBe([Reliance, Armed1]);
        after.Members.Select(m => m.InstrumentId).ShouldBe([Reliance]);
        ActiveSetBuilder.Diff(before, after).Unsubscribe.Select(m => m.InstrumentId).ShouldBe([Armed1]);
    }

    [Fact]
    public async Task A_ticket_with_no_dhan_id_is_not_proposed()
    {
        Armed(new ArmedTicket(5, 1, 1, "NSE:AAA"), new ArmedTicket(6, 1, 2, "NSE:BBB"));
        Known(new InstrumentSecurity("NSE:BBB", Armed2, "778"));

        var candidates = await Provider().ListAsync(Ct);

        candidates.ShouldHaveSingleItem().InstrumentId.ShouldBe(Armed2);
    }

    [Fact]
    public async Task A_failure_reading_the_plan_record_proposes_nothing_and_does_not_fail_the_set()
    {
        _armed.ListAsync(Arg.Any<DateOnly>(), Ct).ThrowsAsync(new InvalidOperationException("db"));

        var candidates = await Provider().ListAsync(Ct);

        candidates.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_nothing_armed_it_proposes_nothing_and_does_not_ask_for_instruments()
    {
        Armed();

        var candidates = await Provider().ListAsync(Ct);

        candidates.ShouldBeEmpty();
        await _securities.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public void With_no_armed_candidate_the_set_is_exactly_the_set_of_the_other_providers()
    {
        var others = new[] { Alert(Guid.NewGuid(), "12"), Index(Nifty, "13"), Holding(Reliance, "2885", 5000m), Holding(Infosys, "1594", 100m) };

        var plain = ActiveSetBuilder.Build(others, 3);
        var withEmptyProvider = ActiveSetBuilder.Build([.. others, .. Array.Empty<ActiveSetCandidate>()], 3);

        withEmptyProvider.Members.ShouldBe(plain.Members);
        withEmptyProvider.Cut.ShouldBe(plain.Cut);
        plain.Cut.ShouldBe(1);
    }

    [Fact]
    public void When_the_cap_cuts_the_armed_ticket_is_the_one_refused_and_no_other_member_is_displaced()
    {
        var armed = new ActiveSetCandidate(Armed1, FeedSegments.NseEquity, "777", FeedMode.Quote, ActiveSetReason.ArmedTicket, 1m);
        var others = new[] { Alert(Guid.NewGuid(), "12"), Index(Nifty, "13"), Holding(Reliance, "2885", 1m) };

        var set = ActiveSetBuilder.Build([armed, .. others], 3);

        set.Members.Select(m => m.InstrumentId).ShouldBe(ActiveSetBuilder.Build(others, 3).Members.Select(m => m.InstrumentId));
        set.Members.ShouldNotContain(m => m.InstrumentId == Armed1);
        set.Cut.ShouldBe(1);
    }

    [Fact]
    public void Armed_tickets_order_among_themselves_by_rank_and_after_holdings()
    {
        var first = new ActiveSetCandidate(Armed1, FeedSegments.NseEquity, "777", FeedMode.Quote, ActiveSetReason.ArmedTicket, 1m);
        var second = new ActiveSetCandidate(Armed2, FeedSegments.NseEquity, "778", FeedMode.Quote, ActiveSetReason.ArmedTicket, 0.5m);

        var set = ActiveSetBuilder.Build([second, first, Holding(Reliance, "2885", 1m)], 200);

        set.Members.Select(m => m.InstrumentId).ShouldBe([Reliance, Armed1, Armed2]);
    }

    [Fact]
    public void A_held_share_that_is_also_armed_keeps_its_holding_place_and_both_reasons()
    {
        var armed = new ActiveSetCandidate(Reliance, FeedSegments.NseEquity, "2885", FeedMode.Quote, ActiveSetReason.ArmedTicket, 1m);

        var set = ActiveSetBuilder.Build([Holding(Reliance, "2885", 5m), Holding(Infosys, "1594", 100m), armed], 200);

        set.Members.Select(m => m.InstrumentId).ShouldBe([Infosys, Reliance]);
        set.Members[1].Reasons.ShouldBe(ActiveSetReason.Holding | ActiveSetReason.ArmedTicket);
    }
}
