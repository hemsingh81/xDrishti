#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Planning;
using XDrishti.Application.Tests.LearnedSettings;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>AU-16 criteria 1 and 2: the scope refuses unknown ids and needs one entry; only instruments in the scope reach the plan while the gates and the ranking stay as they were (same inputs).</summary>
public sealed class AutoScopeTests
{
    private const string Owner = "owner:me@x.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryScopes : IAutoScopeStore
    {
        public List<StoredAutoScope> Rows { get; } = [];

        public ValueTask<StoredAutoScope?> LatestAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Rows.LastOrDefault());

        public ValueTask<StoredAutoScope> AppendAsync(AutoScope scope, string actor, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var row = new StoredAutoScope(Rows.Count + 1, scope.BasketIds, scope.InstrumentIds, scope.Hash, actor, now);
            Rows.Add(row);
            return ValueTask.FromResult(row);
        }
    }

    private sealed class FixedScope(ResolvedAutoScope? scope) : IAutoScopeSource
    {
        public DateOnly? AsOf { get; private set; }

        public ValueTask<ResolvedAutoScope?> ResolveAsync(DateOnly? asOf, CancellationToken cancellationToken)
        {
            AsOf = asOf;
            return ValueTask.FromResult(scope);
        }
    }

    private sealed class EventLog : Microsoft.Extensions.Logging.ILogger<SelectionPlanBuilder>
    {
        public List<int> Ids { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Ids.Add(eventId.Id);
    }

    private static SelectionPlanBuilder BuilderWith(IReadOnlyList<TicketCandidate> found, IAutoScopeSource? scope, Microsoft.Extensions.Logging.ILogger<SelectionPlanBuilder>? logger = null)
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(XDrishti.Domain.Common.Result.Success(new TicketSourceResult(found, 0, 0, []))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(XDrishti.Domain.Common.Result.Success(WiredPlanFixture.Context())));
        return new SelectionPlanBuilder(source, context, logger ?? NullLogger<SelectionPlanBuilder>.Instance, WiredPlanFixture.Loose, null, null, null, scope);
    }

    [Fact]
    public async Task A_scope_that_covers_every_candidate_gives_the_same_plan_as_no_scope_and_stores_its_hash()
    {
        var found = WiredPlanFixture.Candidates();
        var all = new ResolvedAutoScope("h1", found.Select(c => c.InstrumentId!.Value).ToHashSet());

        var without = (await BuilderWith(found, null).BuildAsync(WiredPlanFixture.Request, Ct)).Value;
        var with = (await BuilderWith(found, new FixedScope(all)).BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        with.InputHash.ShouldBe(without.InputHash);
        with.Items.Select(i => (i.Rank, i.Group, i.Symbol, string.Join(',', i.Reasons))).ShouldBe(without.Items.Select(i => (i.Rank, i.Group, i.Symbol, string.Join(',', i.Reasons))));
        with.BasisFlags!.ScopeHash.ShouldBe("h1");
        without.BasisFlags!.ScopeHash.ShouldBeNull();
    }

    [Fact]
    public async Task The_builder_expands_the_scope_as_of_the_plan_date()
    {
        var found = WiredPlanFixture.Candidates();
        var scope = new FixedScope(new ResolvedAutoScope("h", found.Select(c => c.InstrumentId!.Value).ToHashSet()));

        await BuilderWith(found, scope).BuildAsync(WiredPlanFixture.Request, Ct);

        scope.AsOf.ShouldBe(WiredPlanFixture.Request.ForDate);
    }

    [Fact]
    public async Task A_scope_flagged_before_known_since_is_logged_and_listed_on_the_plan()
    {
        var found = WiredPlanFixture.Candidates();
        var logger = new EventLog();
        var flagged = new ResolvedAutoScope("h", found.Select(c => c.InstrumentId!.Value).ToHashSet(), [Guid.NewGuid()]);

        var plan = (await BuilderWith(found, new FixedScope(flagged), logger).BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        logger.Ids.ShouldContain(4744);
        plan.Items.ShouldAllBe(i => i.Evidence.Unavailable.Contains(SelectionPlanBuilder.ScopeBeforeKnownSince));
    }

    [Fact]
    public async Task Only_instruments_in_the_scope_appear_in_the_plan()
    {
        var found = WiredPlanFixture.Candidates();
        var keep = found.Where(c => c.Draft.Symbol is "AAA" or "BBB").Select(c => c.InstrumentId!.Value).ToHashSet();

        var plan = (await BuilderWith(found, new FixedScope(new ResolvedAutoScope("h2", keep))).BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Select(i => i.Symbol).Distinct().Order().ShouldBe(["AAA", "BBB"]);
    }

    [Fact]
    public void A_candidate_with_no_known_instrument_is_in_no_scope()
    {
        var found = WiredPlanFixture.Candidates();
        var unknown = found[0] with { InstrumentId = null };

        var kept = AutoScopeFilter.Apply([unknown, found[1]], new ResolvedAutoScope("h", new HashSet<Guid> { found[1].InstrumentId!.Value }));

        kept.Select(c => c.Draft.Symbol).ShouldBe([found[1].Draft.Symbol]);
    }

    [Fact]
    public async Task The_resolver_expands_baskets_to_current_members_and_adds_the_stocks()
    {
        var basket = Guid.NewGuid();
        var archived = Guid.NewGuid();
        var member = Guid.NewGuid();
        var stock = Guid.NewGuid();
        var store = new MemoryScopes();
        await store.AppendAsync(AutoScope.Create([basket, archived], [stock]).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(member, null), Member(Guid.NewGuid(), DateTimeOffset.UnixEpoch))));
        baskets.MembersAsync(archived, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(archived, DateTimeOffset.UnixEpoch, Member(Guid.NewGuid(), null))));

        var resolved = await new AutoScopeResolver(store, baskets).ResolveAsync(null, Ct);

        resolved!.InstrumentIds.ShouldBe([member, stock], ignoreOrder: true);
        (await new AutoScopeResolver(new MemoryScopes(), baskets).ResolveAsync(null, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_null_as_of_date_keeps_the_live_behaviour_members_now_by_the_open_ended_stay_and_an_archived_basket_left_out()
    {
        // characterisation of the live read as it was before the as-of overload: the pre-change expected values written out, not the resolver compared with itself
        var basket = Guid.NewGuid();
        var archived = Guid.NewGuid();
        var member = Guid.NewGuid();
        var stock = Guid.NewGuid();
        var store = new MemoryScopes();
        var stored = await store.AppendAsync(AutoScope.Create([basket, archived], [stock]).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(member, null), Member(Guid.NewGuid(), DateTimeOffset.UnixEpoch))));
        baskets.MembersAsync(archived, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(archived, DateTimeOffset.UnixEpoch, Member(Guid.NewGuid(), null))));

        var viaOverload = await new AutoScopeResolver(store, baskets).ResolveAsync(null, Ct);

        viaOverload!.Hash.ShouldBe(stored.Hash);
        viaOverload.InstrumentIds.ShouldBe([member, stock], ignoreOrder: true);
        await baskets.DidNotReceive().MembersAsync(Arg.Any<Guid>(), Arg.Is<DateTimeOffset?>(c => c != null), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_as_of_date_leaves_out_a_basket_archived_on_or_before_it()
    {
        var basket = Guid.NewGuid();
        var day = new DateOnly(2025, 3, 3);
        var store = new MemoryScopes();
        await store.AppendAsync(AutoScope.Create([basket], []).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, BasketMember.CutoffAfter(day), null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, BasketMember.CutoffAfter(day).AddHours(-1), Member(Guid.NewGuid(), null))));

        (await new AutoScopeResolver(store, baskets).ResolveAsync(day, Ct))!.InstrumentIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_as_of_date_expands_the_baskets_to_their_members_at_that_date()
    {
        var basket = Guid.NewGuid();
        var then = Guid.NewGuid();
        var now = Guid.NewGuid();
        var stock = Guid.NewGuid();
        var store = new MemoryScopes();
        await store.AppendAsync(AutoScope.Create([basket], [stock]).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var day = new DateOnly(2025, 3, 3);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(now, null))));
        baskets.MembersAsync(basket, BasketMember.CutoffAfter(day), null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(then, DateTimeOffset.UtcNow))));

        var resolved = await new AutoScopeResolver(store, baskets).ResolveAsync(day, Ct);

        resolved!.InstrumentIds.ShouldBe([then, stock], ignoreOrder: true);
    }

    [Fact]
    public async Task The_resolver_asks_for_the_session_cutoff_and_uses_that_answer_not_the_members_now()
    {
        var basket = Guid.NewGuid();
        var addedAfter = Guid.NewGuid();
        var valid = Guid.NewGuid();
        var session = new DateOnly(2025, 3, 3);
        var store = new MemoryScopes();
        await store.AppendAsync(AutoScope.Create([basket], []).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var baskets = Substitute.For<IBasketQueries>();
        // the basket query reconstructs membership at the cutoff from valid_from / valid_to; only the session's own cutoff answers
        baskets.MembersAsync(basket, BasketMember.CutoffAfter(session), null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(valid, null))));
        baskets.MembersAsync(basket, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(addedAfter, null))));

        var resolved = await new AutoScopeResolver(store, baskets).ResolveAsync(session, Ct);

        resolved!.InstrumentIds.ShouldBe([valid]);
        resolved.InstrumentIds.ShouldNotContain(addedAfter);
        resolved.BasketsBeforeKnownSince.ShouldBeNull();
    }

    [Fact]
    public async Task A_system_basket_asked_before_its_known_since_date_uses_the_first_known_members_and_is_flagged()
    {
        var basket = Guid.NewGuid();
        var first = Guid.NewGuid();
        var session = new DateOnly(2025, 3, 3);
        var store = new MemoryScopes();
        await store.AppendAsync(AutoScope.Create([basket], []).Value, Owner, DateTimeOffset.UnixEpoch, Ct);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.MembersAsync(basket, BasketMember.CutoffAfter(session), null, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(first, null)) with { MembersKnownSince = new DateOnly(2025, 6, 1) }));

        var before = await new AutoScopeResolver(store, baskets).ResolveAsync(session, Ct);

        before!.InstrumentIds.ShouldBe([first]);
        before.BasketsBeforeKnownSince.ShouldBe([basket]);

        baskets.MembersAsync(basket, BasketMember.CutoffAfter(new DateOnly(2025, 6, 2)), null, Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<BasketMembers?>(Members(basket, null, Member(first, null)) with { MembersKnownSince = new DateOnly(2025, 6, 1) }));
        (await new AutoScopeResolver(store, baskets).ResolveAsync(new DateOnly(2025, 6, 2), Ct))!.BasketsBeforeKnownSince.ShouldBeNull();
    }

    // Only the fields the code under test reads are set; the other fields of these wide records are not looked at.
    private static T Blank<T>() => (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static BasketMemberView Member(Guid instrument, DateTimeOffset? validTo) =>
        Blank<BasketMemberView>() with { Instrument = Blank<InstrumentSummary>() with { Id = instrument }, ValidTo = validTo };

    private static BasketMembers Members(Guid id, DateTimeOffset? archivedAt, params BasketMemberView[] items) => new(id, "b", archivedAt, null, items.Length, items);

    private static (SetAutoScopeHandler Handler, MemoryScopes Scopes, FakeAccountEvents Events, Guid Basket, Guid Instrument) Set()
    {
        var scopes = new MemoryScopes();
        var events = new FakeAccountEvents();
        var basket = Guid.NewGuid();
        var instrument = Guid.NewGuid();
        var baskets = Substitute.For<IBasketQueries>();
        baskets.GetAsync(basket, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<BasketView?>(Blank<BasketView>() with { Id = basket }));
        var instruments = Substitute.For<IInstrumentQueries>();
        instruments.GetAsync(instrument, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<InstrumentDetail?>(Detail(instrument)));
        return (new SetAutoScopeHandler(scopes, baskets, instruments, events, events, new FakeTimeProvider(), NullLogger<SetAutoScopeHandler>.Instance), scopes, events, basket, instrument);
    }

    private static InstrumentDetail Detail(Guid id) => Blank<InstrumentDetail>() with { Id = id };

    [Fact]
    public async Task Known_baskets_and_stocks_are_stored_and_audited()
    {
        var t = Set();

        var result = await t.Handler.HandleAsync(new SetAutoScopeCommand(Owner, [t.Basket], [t.Instrument]), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSet.ShouldBeTrue();
        result.Value.ChangedBy.ShouldBe(Owner);
        t.Scopes.Rows.Count.ShouldBe(1);
        t.Events.Recorded.Single().Event.ShouldBe(AccountEventType.EssentialSettingChanged);
    }

    [Fact]
    public async Task An_unknown_id_an_empty_scope_and_a_non_owner_are_refused_and_nothing_is_stored()
    {
        var t = Set();

        var unknown = await t.Handler.HandleAsync(new SetAutoScopeCommand(Owner, [Guid.NewGuid()], [t.Instrument]), Ct);
        var unknownStock = await t.Handler.HandleAsync(new SetAutoScopeCommand(Owner, [t.Basket], [Guid.NewGuid()]), Ct);
        var empty = await t.Handler.HandleAsync(new SetAutoScopeCommand(Owner, [], []), Ct);
        var other = await t.Handler.HandleAsync(new SetAutoScopeCommand("auto", [t.Basket], []), Ct);

        unknown.Error.Code.ShouldBe(SetAutoScopeHandler.UnknownCode);
        unknownStock.Error.Code.ShouldBe(SetAutoScopeHandler.UnknownCode);
        empty.Error.Code.ShouldBe(AutoScope.EmptyCode);
        other.Error.Code.ShouldBe("auto.owner_only");
        t.Scopes.Rows.ShouldBeEmpty();
        t.Events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_no_scope_set_the_scope_reads_eligible()
    {
        var view = await new GetAutoScopeHandler(new MemoryScopes()).HandleAsync(new GetAutoScopeQuery(), Ct);

        (view.IsSet, view.Summary).ShouldBe((false, "eligible"));
    }
}
