using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P1-15: membership (many-to-many, dated, ordered), tracking on add, copy and move, and concurrent edits, on a real PostgreSQL.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketMembersApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    [Fact]
    public async Task An_instrument_can_be_in_many_baskets_and_the_instrument_lists_them_in_the_owners_order()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var choice = await BasketAsync("MyChoice");
        var best = await BasketAsync("Best stocks");

        await AddAsync(best.Id, tcs.Id);
        await AddAsync(choice.Id, tcs.Id);

        var baskets = await BasketsOfAsync(tcs.Id);
        baskets.Select(b => (b.BasketId, b.Name, b.Colour, b.Purpose, b.Position)).ShouldBe([(choice.Id, "MyChoice", "#6366f1", "trading", 0), (best.Id, "Best stocks", "#f59e0b", "trading", 2)]);
        baskets.ShouldAllBe(b => b.Since > Now.AddMinutes(-1) && b.Since < Now.AddMinutes(10));
        (await ListBasketsAsync()).Select(b => b.MemberCount).ShouldBe([1, 0, 1]);
    }

    [Fact]
    public async Task An_instrument_is_in_a_basket_once_adding_again_is_idempotent_and_the_database_enforces_it()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var choice = await BasketAsync("MyChoice");

        var first = await AddAsync(choice.Id, tcs.Id, reliance.Id);
        var again = await AddAsync(choice.Id, reliance.Id, tcs.Id, tcs.Id);

        first.ShouldBe(new AddMembersResult(2, 2, 0, 2, 2));
        again.ShouldBe(new AddMembersResult(2, 0, 2, 0, 2));
        (await MembersAsync(choice.Id)).Total.ShouldBe(2);
        var duplicate = await Should.ThrowAsync<PostgresException>(() => Factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"INSERT INTO market.basket_members (id, basket_id, instrument_id, position, valid_from, added_by) VALUES ({Guid.NewGuid()}, {choice.Id}, {tcs.Id}, 9, now(), 'probe')", Ct)));
        duplicate.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Members_come_back_in_the_order_they_were_added_with_the_instrument_summary_and_tracked_block()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var sbin = await FindAsync("NSE:SBIN");
        var choice = await BasketAsync("MyChoice");

        await AddAsync(choice.Id, "from the scan", sbin.Id, tcs.Id);
        await AddAsync(choice.Id, reliance.Id);

        var members = await MembersAsync(choice.Id);
        (members.BasketId, members.Name, members.ArchivedAt, members.AsOf, members.Total).ShouldBe((choice.Id, "MyChoice", null, null, 3));
        members.Items.Select(m => (m.Instrument.Symbol, m.Position, m.Note, m.AddedBy)).ShouldBe(
        [
            ("NSE:SBIN", 0, "from the scan", ApiFactory.OwnerEmail),
            ("NSE:TCS", 1, "from the scan", ApiFactory.OwnerEmail),
            ("NSE:RELIANCE", 2, null, ApiFactory.OwnerEmail),
        ]);
        var first = members.Items[0];
        first.ValidFrom.ShouldBe(Now, Tolerance);
        first.ValidTo.ShouldBeNull();
        first.Instrument.ShouldBe(sbin with { Tracked = first.Instrument.Tracked });
        first.Instrument.Tracked.ShouldSatisfyAllConditions(t => t.Active.ShouldBeTrue(), t => t.Since.ShouldNotBeNull().ShouldBe(Now, Tolerance), t => t.RetiredButTracked.ShouldBeFalse());
    }

    [Fact]
    public async Task Removing_keeps_the_history_adding_again_makes_a_new_stay_and_the_instrument_stays_tracked()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, "first", tcs.Id);
        Factory.Clock.Pin(Now.AddDays(2));
        await ResignInAsync();

        var removed = await RemoveAsync(choice.Id, tcs.Id);
        var removedAgain = await RemoveAsync(choice.Id, tcs.Id);

        removed.ShouldBe(new RemoveMembersResult(1, 1, 0, 0));
        removedAgain.ShouldBe(new RemoveMembersResult(1, 0, 1, 0));
        (await FindAsync("NSE:TCS")).Tracked.Active.ShouldBeTrue("removing the last membership does not untrack");
        Factory.Clock.Pin(Now.AddDays(4));
        await ResignInAsync();
        (await AddAsync(choice.Id, "second", tcs.Id)).Added.ShouldBe(1);

        var history = await HistoryAsync(choice.Id);
        history.Items.Select(e => (e.Kind, e.Symbol, e.Note, e.By)).ShouldBe(
        [
            ("added", "NSE:TCS", "second", ApiFactory.OwnerEmail),
            ("removed", "NSE:TCS", null, ApiFactory.OwnerEmail),
            ("added", "NSE:TCS", "first", ApiFactory.OwnerEmail),
        ]);
        history.Items.Select(e => e.At.Date).ShouldBe([Now.AddDays(4).Date, Now.AddDays(2).Date, Now.Date]);
        history.Items[0].MemberId.ShouldNotBe(history.Items[2].MemberId, "a new stay is a new row");
        (await Factory.WithDbAsync(db => db.BasketMembers.CountAsync(m => m.BasketId == choice.Id, Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task As_of_a_date_gives_the_members_at_the_end_of_that_india_day()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, tcs.Id);                        // 2026-10-11, 09:30 in India
        Factory.Clock.Pin(Now.AddDays(2));                        // 2026-10-13
        await ResignInAsync();
        await AddAsync(choice.Id, reliance.Id);
        await RemoveAsync(choice.Id, tcs.Id);
        Factory.Clock.Pin(Now.AddDays(4));                        // 2026-10-15
        await ResignInAsync();
        await AddAsync(choice.Id, tcs.Id);                        // a second stay

        var onTenth = await MembersAsync(choice.Id, "2026-10-10");
        var onEleventh = await MembersAsync(choice.Id, "2026-10-11");
        var onTwelfth = await MembersAsync(choice.Id, "2026-10-12");
        var onThirteenth = await MembersAsync(choice.Id, "2026-10-13");
        var onFourteenth = await MembersAsync(choice.Id, "2026-10-14");
        var onFifteenth = await MembersAsync(choice.Id, "2026-10-15");
        var current = await MembersAsync(choice.Id);

        onTenth.Items.ShouldBeEmpty();
        (onTenth.AsOf, onTenth.Total).ShouldBe((new DateOnly(2026, 10, 10), 0));
        onEleventh.Items.Select(m => m.Instrument.Symbol).ShouldBe(["NSE:TCS"]);
        onEleventh.Items[0].ValidTo.ShouldNotBeNull().ShouldBe(Now.AddDays(2), Tolerance);
        onTwelfth.Items.Select(m => m.Instrument.Symbol).ShouldBe(["NSE:TCS"]);
        onThirteenth.Items.Select(m => m.Instrument.Symbol).ShouldBe(["NSE:RELIANCE"], "TCS left on the 13th, RELIANCE came on the 13th");
        onFourteenth.Items.Select(m => m.Instrument.Symbol).ShouldBe(["NSE:RELIANCE"]);
        onFifteenth.Items.Select(m => m.Instrument.Symbol).Order().ShouldBe(["NSE:RELIANCE", "NSE:TCS"]);
        current.Items.Select(m => m.Instrument.Symbol).Order().ShouldBe(["NSE:RELIANCE", "NSE:TCS"]);
        current.AsOf.ShouldBeNull();
        (await MembersAsync(choice.Id, "2031-01-01")).Items.Count.ShouldBe(2, "a future date is the same as now");
    }

    [Fact]
    public async Task A_bad_as_of_date_is_a_400_with_the_field()
    {
        var choice = await BasketAsync("MyChoice");

        foreach (var asOf in new[] { "yesterday", "2026-13-40", "11-10-2026", "1999-01-01" })
        {
            using var response = await Client.GetAsync($"/api/baskets/{choice.Id}/members?asOf={asOf}", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var (code, errors) = await ProblemAsync(response);
            (code, errors.ContainsKey("asOf")).ShouldBe(("baskets.invalid", true));
        }
    }

    [Fact]
    public async Task Adding_untracked_instruments_tracks_them_in_the_same_transaction_and_both_are_audited()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        var choice = await BasketAsync("MyChoice");
        await TrackAsync(tcs.Id);
        Factory.Clock.Advance(TimeSpan.FromMinutes(1));

        var result = await AddAsync(choice.Id, tcs.Id, reliance.Id);

        result.ShouldBe(new AddMembersResult(2, 2, 0, 1, 2));
        (await SearchAsync("?tracked=true")).Items.Select(i => i.Symbol).Order().ShouldBe(["NSE:RELIANCE", "NSE:TCS"]);
        (await MembersAsync(choice.Id)).Items.ShouldAllBe(m => m.Instrument.Tracked.Active);
        var events = await Factory.WithDbAsync(db => db.AccountEvents.AsNoTracking()
            .Where(e => e.Event == AccountEventType.InstrumentsTracked || e.Event == AccountEventType.BasketMembersAdded).OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(Ct));
        events.Select(e => (e.Event, e.Detail)).ShouldBe(
        [
            (AccountEventType.InstrumentsTracked, "Tracked 1 of 1 instruments (1 new, 0 tracked again, 0 already tracked)"),
            (AccountEventType.InstrumentsTracked, "Tracked 1 of 2 instruments (1 new, 0 tracked again, 1 already tracked) via basket 'MyChoice'"),
            (AccountEventType.BasketMembersAdded, "Added 2 of 2 instruments to basket 'MyChoice' (0 were members already, 1 tracked now)"),
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task The_member_rows_the_tracking_row_and_the_audit_entries_of_one_add_are_written_by_one_transaction()
    {
        await ImportAsync();
        var reliance = await FindAsync("NSE:RELIANCE");
        var choice = await BasketAsync("MyChoice");

        await AddAsync(choice.Id, reliance.Id);

        var transactions = await Factory.WithDbAsync(db => db.Database.SqlQuery<string>($"""
            SELECT DISTINCT xid AS "Value" FROM (
                SELECT xmin::text AS xid FROM market.basket_members WHERE basket_id = {choice.Id}
                UNION ALL SELECT xmin::text FROM market.tracked_instruments WHERE instrument_id = {reliance.Id}
                UNION ALL SELECT xmin::text FROM accounts.account_events WHERE event IN ('InstrumentsTracked', 'BasketMembersAdded')) t
            """).ToListAsync(Ct));
        transactions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_retired_instrument_that_is_not_tracked_is_refused_by_name_and_nothing_in_the_call_is_applied()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var future = await FindAsync("NSE:RELIANCE-2026-10-FUT");
        var choice = await BasketAsync("MyChoice");
        await RetireFutureAsync();

        using var response = await AddRawAsync(choice.Id, new { instrumentIds = new[] { tcs.Id, future.Id } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (code, errors) = await ProblemAsync(response);
        code.ShouldBe("instruments.retired");
        errors["instrumentIds"].Single().ShouldContain("NSE:RELIANCE-2026-10-FUT");
        (await MembersAsync(choice.Id)).Items.ShouldBeEmpty();
        (await SearchAsync("?tracked=true")).Items.ShouldBeEmpty();
        (await BasketEventsAsync()).ShouldBeEmpty();
        (await ListBasketsAsync()).First().MemberCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_retired_instrument_that_is_tracked_can_be_added_and_is_flagged()
    {
        await ImportAsync();
        var future = await FindAsync("NSE:RELIANCE-2026-10-FUT");
        await TrackAsync(future.Id);
        await RetireFutureAsync();
        var choice = await BasketAsync("MyChoice");

        var result = await AddAsync(choice.Id, future.Id);

        result.ShouldBe(new AddMembersResult(1, 1, 0, 0, 1));
        var member = (await MembersAsync(choice.Id)).Items.Single();
        (member.Instrument.Retired, member.Instrument.Tracked.RetiredButTracked).ShouldBe((true, true));
    }

    [Fact]
    public async Task Unknown_ids_and_bad_lists_are_400_and_change_nothing()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var choice = await BasketAsync("MyChoice");

        using var unknown = await AddRawAsync(choice.Id, new { instrumentIds = new[] { tcs.Id, Guid.NewGuid() } });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(unknown)).Code.ShouldBe("instruments.invalid");

        foreach (var body in new object?[]
        {
            new { instrumentIds = Array.Empty<Guid>() },
            new { },
            new { instrumentIds = new[] { Guid.Empty } },
            new { instrumentIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() },
        })
        {
            using var add = await AddRawAsync(choice.Id, body);
            add.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemAsync(add)).Code.ShouldBe("baskets.invalid");
            using var delete = await SendAsync(HttpMethod.Delete, $"/api/baskets/{choice.Id}/members", body);
            delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var longNote = await AddRawAsync(choice.Id, new { instrumentIds = new[] { tcs.Id }, note = new string('n', 501) });
        longNote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(longNote)).Errors.ShouldContainKey("note");

        using var noBody = await Client.PostAsync($"/api/baskets/{choice.Id}/members", Ct);
        noBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MembersAsync(choice.Id)).Items.ShouldBeEmpty();
        (await SearchAsync("?tracked=true")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_basket_holds_at_most_two_thousand_instruments()
    {
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync("""
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            SELECT gen_random_uuid(), 'NSE:BULK' || g, 'NSE', 'NSE_EQ', 'share', 'Bulk ' || g, 'Bulk ' || g, 1, 'BULK' || g, false, now(), now()
            FROM generate_series(1, 2001) AS g
            """, Ct));
        (await SearchAsync("?q=NSE:BULK&pageSize=1")).Total.ShouldBe(2001);
        var ids = await Factory.WithDbAsync(db => db.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM market.instruments ORDER BY canonical_symbol").ToListAsync(Ct));
        var choice = await BasketAsync("MyChoice");

        foreach (var chunk in ids.Take(2000).Chunk(500))
        {
            (await AddAsync(choice.Id, chunk)).Added.ShouldBe(500);
        }

        using var tooMany = await AddRawAsync(choice.Id, new { instrumentIds = new[] { ids[2000] } });
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (code, errors) = await ProblemAsync(tooMany);
        code.ShouldBe("baskets.too_many_members");
        errors["instrumentIds"].Single().ShouldContain("2,000");
        (await AddAsync(choice.Id, ids[0])).AlreadyMembers.ShouldBe(1, "adding what is there is not a new member");
        var members = await MembersAsync(choice.Id);
        members.Total.ShouldBe(BasketLimits.MaxActiveMembers);
        using var reorder = await Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { instrumentIds = members.Items.Select(m => m.Instrument.Id).Reverse() }, Ct);
        reorder.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reordering_members_renumbers_them_is_idempotent_and_the_order_survives_removal_and_adds()
    {
        await ImportAsync();
        var ids = await SharesAsync(5);
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, ids);
        var wanted = new[] { ids[3], ids[1], ids[4], ids[0], ids[2] };

        using var first = await Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { instrumentIds = wanted }, Ct);
        using var second = await Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { instrumentIds = wanted }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var members = await MembersAsync(choice.Id);
        members.Items.Select(m => m.Instrument.Id).ShouldBe(wanted);
        members.Items.Select(m => m.Position).ShouldBe([0, 1, 2, 3, 4]);
        (await BasketEventsAsync()).Count(e => e.Event == AccountEventType.BasketMembersReordered).ShouldBe(1);

        await RemoveAsync(choice.Id, ids[1]);
        var extra = (await SharesAsync(6))[5];
        await AddAsync(choice.Id, extra);
        (await MemberIdsAsync(choice.Id)).ShouldBe([ids[3], ids[4], ids[0], ids[2], extra]);
    }

    [Fact]
    public async Task A_member_order_that_is_not_exactly_the_current_members_is_a_400_and_changes_nothing()
    {
        await ImportAsync();
        var ids = await SharesAsync(3);
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, ids[0], ids[1]);
        await RemoveAsync(choice.Id, ids[1]);

        foreach (var order in new[] { Array.Empty<Guid>(), [ids[0], ids[1]], [ids[0], ids[2]], [ids[0], ids[0]], [ids[0], Guid.Empty] })
        {
            using var response = await Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { instrumentIds = order }, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var (code, errors) = await ProblemAsync(response);
            (code, errors.ContainsKey("instrumentIds")).ShouldBe(("baskets.invalid", true));
        }

        using var missing = await Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { }, Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MemberIdsAsync(choice.Id)).ShouldBe([ids[0]]);
    }

    [Fact]
    public async Task Copy_adds_to_the_target_and_leaves_the_source_move_also_ends_the_stay_in_the_source()
    {
        await ImportAsync();
        var ids = await SharesAsync(4);
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        var best = await BasketAsync("Best stocks");
        await AddAsync(choice.Id, "why", ids);

        var copied = await TransferAsync(choice.Id, longTerm.Id, "copy", ids[2], ids[0]);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var moved = await TransferAsync(choice.Id, best.Id, "move", ids[1], ids[3]);

        copied.ShouldBe(new TransferResult("copy", 2, 2, 0, 0, 0, 0, 4, 2));
        moved.ShouldBe(new TransferResult("move", 2, 2, 0, 2, 0, 0, 2, 2));
        (await MemberIdsAsync(choice.Id)).ShouldBe([ids[0], ids[2]]);
        (await MemberIdsAsync(longTerm.Id)).ShouldBe([ids[0], ids[2]], "in the source's order, not the order of the request");
        (await MemberIdsAsync(best.Id)).ShouldBe([ids[1], ids[3]]);
        (await MembersAsync(best.Id)).Items.ShouldAllBe(m => m.Note == "why");
        var removedFromSource = (await HistoryAsync(choice.Id)).Items.Where(e => e.Kind == "removed").ToList();
        removedFromSource.Count.ShouldBe(2);
        (await BasketsOfAsync(ids[1])).Select(b => b.Name).ShouldBe(["Best stocks"]);
        (await BasketsOfAsync(ids[0])).Select(b => b.Name).ShouldBe(["MyChoice", "MyLongTerm"]);
        var audit = (await BasketEventsAsync()).Where(e => e.Event == AccountEventType.BasketMembersTransferred).ToList();
        audit.Select(e => e.Detail).ShouldBe(
        [
            "Copied 2 of 2 instruments from basket 'MyChoice' to basket 'MyLongTerm' (0 already there, 0 not in the source, 0 tracked now)",
            "Moved 2 of 2 instruments from basket 'MyChoice' to basket 'Best stocks' (0 already there, 0 not in the source, 0 tracked now)",
        ]);
    }

    [Fact]
    public async Task Repeating_a_transfer_changes_nothing_and_writes_no_new_audit_entry()
    {
        await ImportAsync();
        var ids = await SharesAsync(3);
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        await AddAsync(choice.Id, ids);
        await TransferAsync(choice.Id, longTerm.Id, "copy", ids);
        var events = (await BasketEventsAsync()).Count;

        var copyAgain = await TransferAsync(choice.Id, longTerm.Id, "copy", ids);
        var moved = await TransferAsync(choice.Id, longTerm.Id, "move", ids);
        var events2 = (await BasketEventsAsync()).Count;
        var moveAgain = await TransferAsync(choice.Id, longTerm.Id, "move", ids);

        copyAgain.ShouldBe(new TransferResult("copy", 3, 0, 3, 0, 0, 0, 3, 3));
        moved.ShouldBe(new TransferResult("move", 3, 0, 3, 3, 0, 0, 0, 3), "they are in the target already, so the move only removes them from the source");
        moveAgain.ShouldBe(new TransferResult("move", 3, 0, 0, 0, 3, 0, 0, 3));
        events2.ShouldBe(events + 1);
        (await BasketEventsAsync()).Count.ShouldBe(events2);
        (await MemberIdsAsync(longTerm.Id)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task A_transfer_is_all_or_nothing_a_retired_instrument_stops_it_before_anything_moves()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var future = await FindAsync("NSE:RELIANCE-2026-10-FUT");
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        await AddAsync(choice.Id, tcs.Id, future.Id);
        await RetireFutureAsync();
        await UntrackAsync(future.Id);

        using var response = await TransferRawAsync(new { fromBasketId = choice.Id, toBasketId = longTerm.Id, instrumentIds = new[] { tcs.Id, future.Id }, mode = "move" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(response)).Code.ShouldBe("instruments.retired");
        (await MemberIdsAsync(choice.Id)).Order().ShouldBe(new[] { tcs.Id, future.Id }.Order());
        (await MemberIdsAsync(longTerm.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_transfer_only_moves_members_of_the_source_and_bad_requests_are_400()
    {
        await ImportAsync();
        var ids = await SharesAsync(2);
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        await AddAsync(choice.Id, ids[0]);

        var result = await TransferAsync(choice.Id, longTerm.Id, "copy", ids[0], ids[1]);

        result.ShouldBe(new TransferResult("copy", 2, 1, 0, 0, 1, 0, 1, 1));
        (await MemberIdsAsync(longTerm.Id)).ShouldBe([ids[0]]);
        foreach (var body in new object[]
        {
            new { fromBasketId = choice.Id, toBasketId = longTerm.Id, instrumentIds = ids, mode = "swap" },
            new { fromBasketId = choice.Id, toBasketId = longTerm.Id, instrumentIds = ids },
            new { fromBasketId = choice.Id, toBasketId = choice.Id, instrumentIds = ids, mode = "copy" },
            new { fromBasketId = choice.Id, toBasketId = longTerm.Id, instrumentIds = Array.Empty<Guid>(), mode = "copy" },
            new { fromBasketId = Guid.Empty, toBasketId = longTerm.Id, instrumentIds = ids, mode = "copy" },
            new { },
        })
        {
            using var response = await TransferRawAsync(body);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemAsync(response)).Code.ShouldBe("baskets.invalid");
        }
    }

    [Fact]
    public async Task Transferring_tracks_what_the_target_gets_in_the_same_transaction()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        await AddAsync(choice.Id, tcs.Id);
        await UntrackAsync(tcs.Id);

        var result = await TransferAsync(choice.Id, longTerm.Id, "copy", tcs.Id);

        result.NewlyTracked.ShouldBe(1);
        (await FindAsync("NSE:TCS")).Tracked.Active.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_adds_of_different_instruments_to_one_basket_all_succeed_with_distinct_positions()
    {
        await ImportAsync();
        var ids = await SharesAsync(12);
        var choice = await BasketAsync("MyChoice");

        var responses = await Task.WhenAll(ids.Chunk(3).Select(chunk => AddRawAsync(choice.Id, new { instrumentIds = chunk })));

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        var members = await MembersAsync(choice.Id);
        members.Items.Select(m => m.Instrument.Id).Order().ShouldBe(ids.Order());
        members.Items.Select(m => m.Position).Distinct().Count().ShouldBe(12);
        (await Factory.WithDbAsync(db => db.Baskets.AsNoTracking().Where(b => b.Id == choice.Id).Select(b => b.Revision).SingleAsync(Ct))).ShouldBe(4);
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_instruments_add_each_once()
    {
        await ImportAsync();
        var ids = await SharesAsync(10);
        var choice = await BasketAsync("MyChoice");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => AddAsync(choice.Id, ids)));

        results.Sum(r => r.Added).ShouldBe(10);
        results.ShouldAllBe(r => r.Added + r.AlreadyMembers == 10);
        (await Factory.WithDbAsync(db => db.BasketMembers.CountAsync(m => m.BasketId == choice.Id && m.ValidTo == null, Ct))).ShouldBe(10);
        (await Factory.WithDbAsync(db => db.TrackedInstruments.CountAsync(Ct))).ShouldBe(10);
    }

    [Fact]
    public async Task Concurrent_reorders_leave_one_of_the_orders_with_positions_zero_to_n_minus_one()
    {
        await ImportAsync();
        var ids = await SharesAsync(6);
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, ids);
        var orders = new[]
        {
            ids.Reverse().ToArray(),
            [ids[1], ids[0], ids[3], ids[2], ids[5], ids[4]],
            [ids[2], ids[4], ids[0], ids[5], ids[1], ids[3]],
            [ids[5], ids[3], ids[1], ids[4], ids[2], ids[0]],
        };

        var responses = await Task.WhenAll(orders.Select(order => Client.PutJsonAsync($"/api/baskets/{choice.Id}/members/order", new { instrumentIds = order }, Ct)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.NoContent);
        var members = await MembersAsync(choice.Id);
        orders.ShouldContain(o => o.SequenceEqual(members.Items.Select(m => m.Instrument.Id)));
        members.Items.Select(m => m.Position).ShouldBe([0, 1, 2, 3, 4, 5]);
    }

    [Fact]
    public async Task Concurrent_moves_of_the_same_instruments_move_them_once_and_leave_a_consistent_state()
    {
        await ImportAsync();
        var ids = await SharesAsync(5);
        var choice = await BasketAsync("MyChoice");
        var longTerm = await BasketAsync("MyLongTerm");
        await AddAsync(choice.Id, ids);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TransferAsync(choice.Id, longTerm.Id, "move", ids)));

        results.Sum(r => r.Removed).ShouldBe(5);
        results.Sum(r => r.Added).ShouldBe(5);
        (await MemberIdsAsync(choice.Id)).ShouldBeEmpty();
        (await MemberIdsAsync(longTerm.Id)).Order().ShouldBe(ids.Order());
        (await Factory.WithDbAsync(db => db.BasketMembers.CountAsync(m => m.ValidTo == null, Ct))).ShouldBe(5);
    }

    [Fact]
    public async Task A_stale_revision_is_refused_by_the_database_so_a_lost_update_cannot_happen()
    {
        var choice = await BasketAsync("MyChoice");

        var changed = await Factory.WithDbAsync(async db =>
        {
            var basket = await db.Baskets.SingleAsync(b => b.Id == choice.Id, Ct);
            await db.Database.ExecuteSqlAsync($"UPDATE market.baskets SET revision = revision + 1 WHERE id = {choice.Id}", Ct); // a concurrent writer
            basket.Touch();
            return await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync(Ct));
        });

        changed.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_save_that_relied_on_a_tracked_row_is_refused_when_an_untrack_committed_first_and_the_untrack_stands()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);

        // Request A has read TCS as tracked and collects its change (a basket member) on that basis; request B untracks it and commits first.
        using var scope = Factory.Services.CreateScope();
        var rows = scope.ServiceProvider.GetRequiredService<ITrackedInstrumentStore>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IMarketUnitOfWork>();
        var tracked = (await rows.FindAsync([tcs.Id], Ct)).Single();
        tracked.Active.ShouldBeTrue();
        rows.RelyOn(tracked);
        await UntrackAsync(tcs.Id);

        (await unitOfWork.TrySaveAsync(Ct)).ShouldBeFalse();

        (await Factory.WithDbAsync(db => db.TrackedInstruments.AsNoTracking().SingleAsync(t => t.Id == tcs.Id, Ct))).Active.ShouldBeFalse();
    }

    [Fact]
    public async Task A_save_that_relied_on_a_tracked_row_that_nobody_changed_goes_through_and_a_later_save_based_on_the_old_version_is_refused()
    {
        await ImportAsync();
        var tcs = await FindAsync("NSE:TCS");
        await TrackAsync(tcs.Id);
        using var first = Factory.Services.CreateScope();
        using var second = Factory.Services.CreateScope();
        var firstRows = first.ServiceProvider.GetRequiredService<ITrackedInstrumentStore>();
        var secondRows = second.ServiceProvider.GetRequiredService<ITrackedInstrumentStore>();
        firstRows.RelyOn((await firstRows.FindAsync([tcs.Id], Ct)).Single());
        secondRows.RelyOn((await secondRows.FindAsync([tcs.Id], Ct)).Single());

        var winner = await first.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct);
        var loser = await second.ServiceProvider.GetRequiredService<IMarketUnitOfWork>().TrySaveAsync(Ct);

        (winner, loser).ShouldBe((true, false)); // each reliance is a write of the row, so two requests relying on the same row take turns
        (await Factory.WithDbAsync(db => db.TrackedInstruments.AsNoTracking().SingleAsync(t => t.Id == tcs.Id, Ct))).Active.ShouldBeTrue();
    }

    [Fact]
    public async Task The_history_lists_the_newest_entries_first_and_takes_a_limit()
    {
        await ImportAsync();
        var ids = await SharesAsync(3);
        var choice = await BasketAsync("MyChoice");
        await AddAsync(choice.Id, ids);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await RemoveAsync(choice.Id, ids[0]);

        var all = await HistoryAsync(choice.Id);
        using var limited = await Client.GetAsync($"/api/baskets/{choice.Id}/history?take=2", Ct);
        var two = await limited.ReadAsync<BasketHistory>(Ct);

        all.Items.Count.ShouldBe(4);
        all.Items[0].Kind.ShouldBe("removed");
        all.Items.Select(e => e.At).ShouldBe(all.Items.Select(e => e.At).OrderDescending());
        two.Items.Select(e => e.MemberId).ShouldBe(all.Items.Take(2).Select(e => e.MemberId));
        foreach (var take in new[] { 0, -1, 1001 })
        {
            using var bad = await Client.GetAsync($"/api/baskets/{choice.Id}/history?take={take}", Ct);
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    private async Task TrackAsync(params Guid[] ids)
    {
        using var response = await Client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = ids }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task UntrackAsync(params Guid[] ids)
    {
        using var response = await SendAsync(HttpMethod.Delete, "/api/instruments/tracked", new { instrumentIds = ids });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
