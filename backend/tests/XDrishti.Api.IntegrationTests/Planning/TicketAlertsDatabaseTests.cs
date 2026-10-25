using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-20 (ADR 0115) on a real database: a crossing of an armed ticket's stored level is stored once (also under concurrency) and not after a mute, the database refuses what the tables refuse, a prompt is stored once and its status follows the stored
/// trades (any source, not a voided one), the bell's source is empty while the setting is OFF and carries the notices while ON, reading writes nothing, and the owner's acts are insert-only rows with an owner actor.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class TicketAlertsDatabaseTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string OwnerActor = "owner:test@example.com";

    private static readonly Guid Account = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7c01");

    private static readonly Guid NoAllocation = Guid.Parse("0b6f2a9e-7a52-4d7e-9c3a-5d0a7e0b7c02");

    private static readonly DateOnly Session = new(2099, 2, 9);

    private static readonly DateTimeOffset Cutoff = new(2099, 2, 9, 3, 35, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset BeforeCutoff = new(2099, 2, 9, 2, 0, 0, TimeSpan.Zero);

    /// <summary>10:00 India time on the session day.</summary>
    private static readonly DateTimeOffset Morning = new(2099, 2, 9, 4, 30, 0, TimeSpan.Zero);

    /// <summary>17:00 India time on the session day.</summary>
    private static readonly DateTimeOffset Evening = new(2099, 2, 9, 11, 30, 0, TimeSpan.Zero);

    private Guid _instrument;

    private int _seq;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = await OpenAsync();
        foreach (var (id, slug, client) in new[] { (Account, "probe-ta1", "1000000071"), (NoAllocation, "probe-ta2", "1000000072") })
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES (@id, @slug, @slug, 'Dhan', @client, current_date, '#1A2B3C', now(), now(), false, false, false, 200)
                """,
                ("id", id), ("slug", slug), ("client", client));
        }

        _instrument = await SeedAsync("ALRT");
    }

    private sealed class Settings(bool enabled) : ITicketAlertSettings
    {
        public bool Enabled => enabled;

        public int PromptTradingDays => 3;

        public int WatchedCacheSeconds => 30;
    }

    private static NewPlanItem Item(int rank, string side, TicketLevels? levels) =>
        new(
            rank, TicketGroup.Primary, "NSE:ALRT", side, "ORB", new string('a', 64), "close above 15m high", "0.8 ATR below entry", "2R", "09:30-11:30", [],
            new PlanEvidence("ORB|15m|" + side + "|intraday|FIXED_RR|*", 41, "replayed history", "cell statistics only", 0.72, 0.12, [], null, 0.7),
            [new NewAllocation(Account, "paper", 10)], levels);

    private static TicketLevels Buy() => TicketLevels.Create("BUY", 100m, Session, 95m, StopAnchor.Level, 110m, 2m).Value;

    private async Task<T> WithAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Publishes a plan, approves each ticket as the owner before the cutoff and arms it, then moves the clock to the morning.</summary>
    private async Task<(long Plan, long[] Items)> ArmedAsync(params TicketLevels?[] levels)
    {
        Factory.Clock.Pin(BeforeCutoff);
        var plan = new NewPlan("alerts-" + Interlocked.Increment(ref _seq), Session, Cutoff, 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch, [.. levels.Select((l, i) => Item(i + 1, "BUY", l))]);
        var published = await WithAsync(p => p.GetRequiredService<IPlanStore>().PublishAsync(plan, Ct).AsTask());
        var items = (await WithAsync(p => p.GetRequiredService<IPlanStore>().ItemsAsync(published.Id, Ct).AsTask())).Select(i => i.Id).ToArray();
        foreach (var item in items)
        {
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(item, TicketState.Approved, OwnerActor, "ok"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
            (await WithAsync(p => p.GetRequiredService<ICommandHandler<MoveTicketCommand, TicketMoveOutcome>>().HandleAsync(new MoveTicketCommand(item, TicketState.Armed, "system:preopen", "gap within limit"), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        }

        Factory.Clock.Pin(Morning);
        return (published.Id, items);
    }

    private TicketAlertObserver Observer() => new(Factory.Services.GetRequiredService<IServiceScopeFactory>(), new Settings(true), Factory.Clock, NullLogger<TicketAlertObserver>.Instance);

    private LivePrice Tick(decimal price, int minute) => new(_instrument, price, Morning.AddMinutes(minute));

    private async Task<string?> StateOfAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync();
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private async Task InsertTradeAsync(string source, string side = "buy", bool voided = false)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var connection = await OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO portfolio.transactions (id, account_id, trading_symbol, exchange, side, trade_date, trade_time, qty, price, charges, source, seq, external_id, created_by, created_at, voided_at, voided_by)
            VALUES (@id, @account, 'ALRT', 'NSE', @side, @date, '10:00', 10, 100, 0, @source, 1, @ext, 'test', now(), CASE WHEN @voided THEN now() END, CASE WHEN @voided THEN 'test' END)
            """,
            ("id", Guid.NewGuid()), ("account", Account), ("side", side), ("date", Session), ("source", source), ("ext", "t" + n.ToString(CultureInfo.InvariantCulture)), ("voided", voided));
    }

    private Task<IReadOnlyList<PromptView>> PromptsAsync(long plan, DateTimeOffset now)
    {
        Factory.Clock.Pin(now);
        return WithAsync<IReadOnlyList<PromptView>>(async p => (await p.GetRequiredService<TicketAlertReader>().ReadAsync(Session, plan, 3, now, Ct)).Prompts);
    }

    private Task<int> StorePromptsAsync() =>
        WithAsync(async p =>
        {
            var store = p.GetRequiredService<ITicketAlertStore>();
            return await store.AddPromptsAsync(await store.ListArmedOnAsync(Session, Ct), Ct);
        });

    [Fact]
    public async Task Ticks_around_the_levels_store_one_stop_and_one_target_and_a_gap_or_a_late_tick_stores_nothing_more()
    {
        var (_, items) = await ArmedAsync(Buy());
        var observer = Observer();

        foreach (var (price, minute) in new[] { (99m, 1), (95m, 2), (94.5m, 3), (110.2m, 4), (94m, 5) })
        {
            await observer.ObserveAsync([Tick(price, minute)], Ct);
        }

        var rows = await WithAsync(p => p.GetRequiredService<ITicketAlertStore>().ListCrossingsAsync(Session, null, Ct).AsTask());
        rows.Select(r => (r.Kind, r.Price, r.Level)).ShouldBe([(CrossingKinds.Stop, 95m, 95m), (CrossingKinds.Target, 110.2m, 110m)]);
        rows.ShouldAllBe(r => r.ItemId == items[0]);

        // A tick before the open and one at the close fire nothing for a fresh ticket.
        var before = await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts");
        await Observer().ObserveAsync([new LivePrice(_instrument, 90m, new DateTimeOffset(2099, 2, 9, 3, 30, 0, TimeSpan.Zero)), new LivePrice(_instrument, 90m, new DateTimeOffset(2099, 2, 9, 10, 0, 0, TimeSpan.Zero))], Ct);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts")).ShouldBe(before);
        (await StateAsync(items[0])).ShouldBe("armed");
    }

    private async Task<string> StateAsync(long item) =>
        await ScalarAsync<string>("SELECT to_state FROM trading.plan_events WHERE item_id = @i ORDER BY id DESC LIMIT 1", ("i", item));

    [Fact]
    public async Task A_ticket_without_numeric_levels_is_not_watched_and_parallel_inserts_store_one_crossing()
    {
        var (plan, items) = await ArmedAsync(null, Buy());
        var store = Factory.Services.GetRequiredService<IServiceScopeFactory>();

        var watched = await WithAsync(p => p.GetRequiredService<ITicketAlertStore>().ListWatchedAsync(Session, Ct).AsTask());
        watched.Select(w => w.ItemId).ShouldBe([items[1]]);

        var crossing = new NewCrossing(plan, items[1], Session, CrossingKinds.Stop, 95m, "level", 94.9m, Morning);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = store.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ITicketAlertStore>().AddCrossingAsync(crossing, Ct);
        }));

        results.Count(r => r).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts")).ShouldBe(1);
    }

    [Fact]
    public async Task Mute_stops_further_crossings_keeps_the_earlier_one_and_is_refused_after_the_session()
    {
        var (plan, items) = await ArmedAsync(Buy());
        await Observer().ObserveAsync([Tick(94m, 1)], Ct);

        var muted = await WithAsync(p => p.GetRequiredService<ICommandHandler<MuteTicketAlertsCommand>>().HandleAsync(new MuteTicketAlertsCommand(items[0], OwnerActor), Ct).AsTask());
        var again = await WithAsync(p => p.GetRequiredService<ICommandHandler<MuteTicketAlertsCommand>>().HandleAsync(new MuteTicketAlertsCommand(items[0], OwnerActor), Ct).AsTask());
        var late = await WithAsync(p => p.GetRequiredService<ITicketAlertStore>().AddCrossingAsync(new NewCrossing(plan, items[0], Session, CrossingKinds.Target, 110m, null, 111m, Morning), Ct).AsTask());

        muted.IsSuccess.ShouldBeTrue();
        again.IsSuccess.ShouldBeTrue();
        late.ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_notice_acks WHERE action = 'mute'")).ShouldBe(1);
        (await WithAsync(p => p.GetRequiredService<ITicketAlertStore>().ListWatchedAsync(Session, Ct).AsTask())).ShouldBeEmpty();

        Factory.Clock.Pin(Evening);
        var after = await WithAsync(p => p.GetRequiredService<ICommandHandler<MuteTicketAlertsCommand>>().HandleAsync(new MuteTicketAlertsCommand(items[0], OwnerActor), Ct).AsTask());
        after.Error.Code.ShouldBe(TicketAlertErrors.AlertClosedCode);
    }

    [Fact]
    public async Task The_database_refuses_a_third_kind_a_non_positive_number_a_foreign_actor_and_a_badly_shaped_act()
    {
        var (plan, items) = await ArmedAsync(Buy());
        const string alert = "INSERT INTO trading.ticket_alerts (plan_id, item_id, session, kind, level, price, tick_at) VALUES (@p, @i, @s, @k, @l, @x, now())";
        (object P, object I, object S) key = (plan, items[0], Session);

        (await StateOfAsync(alert, ("p", key.P), ("i", key.I), ("s", key.S), ("k", "entry_crossed"), ("l", 95m), ("x", 94m))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(alert, ("p", key.P), ("i", key.I), ("s", key.S), ("k", "stop_crossed"), ("l", 0m), ("x", 94m))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(alert, ("p", key.P), ("i", key.I), ("s", key.S), ("k", "stop_crossed"), ("l", 95m), ("x", -1m))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(alert, ("p", plan + 1000), ("i", key.I), ("s", key.S), ("k", "stop_crossed"), ("l", 95m), ("x", 94m))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);

        const string ack = "INSERT INTO trading.ticket_notice_acks (notice_kind, notice_id, item_id, action, until, actor) VALUES (@k, @n, @i, @a, @u, @actor)";
        object none = DBNull.Value;
        (await StateOfAsync(ack, ("k", "crossing"), ("n", 1L), ("i", none), ("a", "dismiss"), ("u", none), ("actor", "system:x"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(ack, ("k", "crossing"), ("n", 1L), ("i", none), ("a", "snooze"), ("u", none), ("actor", OwnerActor))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(ack, ("k", "ticket"), ("n", none), ("i", none), ("a", "mute"), ("u", none), ("actor", OwnerActor))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StateOfAsync(ack, ("k", "crossing"), ("n", 1L), ("i", none), ("a", "dismiss"), ("u", none), ("actor", OwnerActor))).ShouldBeNull();

        const string prompt = "INSERT INTO trading.ticket_prompts (plan_id, item_id, account_id, session) VALUES (@p, @i, @a, @s)";
        (await StateOfAsync(prompt, ("p", plan), ("i", items[0]), ("a", NoAllocation), ("s", Session))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await StateOfAsync(prompt, ("p", plan), ("i", items[0]), ("a", Account), ("s", Session))).ShouldBeNull();
        (await StateOfAsync(prompt, ("p", plan), ("i", items[0]), ("a", Account), ("s", Session))).ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_prompt_is_stored_once_and_its_status_follows_the_stored_trades_of_any_source_but_not_a_voided_one()
    {
        var (plan, _) = await ArmedAsync(Buy());
        (await StorePromptsAsync()).ShouldBe(1);
        (await StorePromptsAsync()).ShouldBe(0);

        (await PromptsAsync(plan, Evening)).ShouldHaveSingleItem().Status.ShouldBe(PromptStatus.Open);

        await InsertTradeAsync("manual", voided: true);
        (await PromptsAsync(plan, Evening)).ShouldHaveSingleItem().Status.ShouldBe(PromptStatus.Open);

        await InsertTradeAsync("manual", side: "sell");
        (await PromptsAsync(plan, Evening)).ShouldHaveSingleItem().Status.ShouldBe(PromptStatus.Open);

        await InsertTradeAsync("manual");
        var matched = (await PromptsAsync(plan, Evening)).ShouldHaveSingleItem();
        matched.Status.ShouldBe(PromptStatus.Matched);
        matched.Via.ShouldBe(PromptVia.TradeOnRecord);
    }

    [Fact]
    public async Task With_no_trade_after_the_window_the_prompt_expires_and_two_tickets_that_fit_one_trade_are_ambiguous()
    {
        var (plan, items) = await ArmedAsync(Buy(), Buy());
        await StorePromptsAsync();

        (await PromptsAsync(plan, Morning.AddDays(10))).ShouldAllBe(p => p.Status == PromptStatus.ExpiredNoFill);

        await InsertTradeAsync("csv");
        var prompts = await PromptsAsync(plan, Evening);
        prompts.ShouldAllBe(p => p.Status == PromptStatus.Ambiguous);
        prompts.Single(p => p.ItemId == items[0]).SameTradeItems.ShouldBe([items[1]]);
        prompts.Single(p => p.ItemId == items[1]).SameTradeItems.ShouldBe([items[0]]);
    }

    [Fact]
    public async Task The_bell_source_is_empty_while_off_carries_the_notices_while_on_honours_snooze_and_dismiss_and_writes_nothing()
    {
        var (plan, items) = await ArmedAsync(Buy());
        await Observer().ObserveAsync([Tick(94m, 1)], Ct);
        await StorePromptsAsync();

        async Task<IReadOnlyList<PlanNotice>> ListAsync(bool enabled)
        {
            Factory.Clock.Pin(Evening);
            using var scope = Factory.Services.CreateScope();
            return await new TicketNoticeSource(scope.ServiceProvider.GetRequiredService<TicketAlertReader>(), new Settings(enabled)).ListAsync(Evening, Ct);
        }

        (await ListAsync(false)).ShouldBeEmpty();
        var counts = (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts"), await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_notice_acks"), await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events"));

        var on = await ListAsync(true);
        var crossing = on.Single(n => n.Kind == "ticket_level_crossed");
        var prompt = on.Single(n => n.Kind == "post_trade_prompt");
        crossing.Id.ShouldStartWith("crossing:");
        crossing.Route.ShouldBe($"/plans?plan={plan}");
        crossing.Text.ShouldNotContain("ALRT");
        prompt.Id.ShouldStartWith("prompt:");
        prompt.Resolved.ShouldBeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_alerts"), await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_notice_acks"), await ScalarAsync<long>("SELECT count(*) FROM trading.plan_events")).ShouldBe(counts);

        (await WithAsync(p => p.GetRequiredService<ICommandHandler<SnoozeTicketNoticeCommand>>().HandleAsync(new SnoozeTicketNoticeCommand(crossing.Id, 30, OwnerActor), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        (await ListAsync(true)).ShouldNotContain(n => n.Id == crossing.Id);
        Factory.Clock.Pin(Evening.AddMinutes(31));
        using (var scope = Factory.Services.CreateScope())
        {
            (await new TicketNoticeSource(scope.ServiceProvider.GetRequiredService<TicketAlertReader>(), new Settings(true)).ListAsync(Evening.AddMinutes(31), Ct)).ShouldContain(n => n.Id == crossing.Id);
        }

        (await WithAsync(p => p.GetRequiredService<ICommandHandler<DismissTicketNoticeCommand>>().HandleAsync(new DismissTicketNoticeCommand(prompt.Id, OwnerActor), Ct).AsTask())).IsSuccess.ShouldBeTrue();
        (await ListAsync(true)).ShouldNotContain(n => n.Id == prompt.Id);

        await InsertTradeAsync("manual");
        _ = items;
    }

    [Fact]
    public async Task The_endpoints_need_a_session_validate_the_notice_id_cap_the_snoozes_and_store_an_owner_actor_from_the_session()
    {
        var (plan, items) = await ArmedAsync(Buy());
        await Observer().ObserveAsync([Tick(94m, 1)], Ct);
        await ResignInAsync();
        var id = await ScalarAsync<long>("SELECT id FROM trading.ticket_alerts");
        var anonymous = Factory.CreateAnonymousClient();

        using var noSession = await anonymous.GetAsync(new Uri($"/api/plans/{plan}/alerts", UriKind.Relative), Ct);
        noSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var view = await Client.GetFromJsonAsync<JsonElement>(new Uri($"/api/plans/{plan}/alerts", UriKind.Relative), Ct);
        view.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        view.GetProperty("crossings").GetArrayLength().ShouldBe(0);
        using var missing = await Client.GetAsync(new Uri("/api/plans/999999/alerts", UriKind.Relative), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var badId = await Client.PostAsJsonAsync(new Uri("/api/plans/alerts/nonsense/dismiss", UriKind.Relative), new { }, Ct);
        using var badMinutes = await Client.PostAsJsonAsync(new Uri($"/api/plans/alerts/crossing:{id}/snooze", UriKind.Relative), new { minutes = 20 }, Ct);
        using var unknown = await Client.PostAsJsonAsync(new Uri("/api/plans/alerts/crossing:9999/dismiss", UriKind.Relative), new { }, Ct);
        using var unknownItem = await Client.PostAsJsonAsync(new Uri("/api/plans/items/999999/alerts/mute", UriKind.Relative), new { }, Ct);
        badId.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        badMinutes.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknownItem.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        for (var i = 0; i < 3; i++)
        {
            using var ok = await Client.PostAsJsonAsync(new Uri($"/api/plans/alerts/crossing:{id}/snooze", UriKind.Relative), new { minutes = 15 }, Ct);
            ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var fourth = await Client.PostAsJsonAsync(new Uri($"/api/plans/alerts/crossing:{id}/snooze", UriKind.Relative), new { minutes = 15 }, Ct);
        fourth.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await fourth.Content.ReadAsStringAsync(Ct)).ShouldContain("plans.snooze_limit");

        using var dismiss = await Client.PostAsJsonAsync(new Uri($"/api/plans/alerts/crossing:{id}/dismiss", UriKind.Relative), new { }, Ct);
        using var mute = await Client.PostAsJsonAsync(new Uri($"/api/plans/items/{items[0]}/alerts/mute", UriKind.Relative), new { }, Ct);
        dismiss.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        mute.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_notice_acks WHERE actor NOT LIKE 'owner:%'")).ShouldBe(0);
        (await ScalarAsync<long>("SELECT count(*) FROM trading.ticket_notice_acks")).ShouldBe(5);
    }
}
