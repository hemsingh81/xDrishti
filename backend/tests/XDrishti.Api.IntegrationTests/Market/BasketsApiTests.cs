using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>P1-15: creating, changing, ordering and archiving baskets on a real PostgreSQL.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class BasketsApiTests(ApiFactory factory) : BasketsTestBase(factory)
{
    [Fact]
    public async Task The_three_baskets_of_the_design_exist_empty_in_order()
    {
        var baskets = await ListBasketsAsync();

        baskets.Select(b => (b.Name, b.Purpose, b.Colour, b.Position, b.MemberCount)).ShouldBe(
        [
            ("MyChoice", "trading", "#6366f1", 0, 0),
            ("MyLongTerm", "long_term", "#10b981", 1, 0),
            ("Best stocks", "trading", "#f59e0b", 2, 0),
        ]);
        baskets.ShouldAllBe(b => b.ArchivedAt == null && b.Description == null);
    }

    [Fact]
    public async Task A_new_basket_is_created_at_the_end_with_its_attributes_and_the_creation_is_audited()
    {
        var created = await CreateBasketAsync("  Watchlist ", "#ABCDEF", "watch", "Names to follow");

        (created.Name, created.Colour, created.Purpose, created.Description, created.Position, created.MemberCount, created.ArchivedAt)
            .ShouldBe(("Watchlist", "#abcdef", "watch", "Names to follow", 3, 0, null));
        created.CreatedAt.ShouldBe(Now, Tolerance);
        (await ListBasketsAsync()).Select(b => b.Name).ShouldBe(["MyChoice", "MyLongTerm", "Best stocks", "Watchlist"]);
        var audit = (await BasketEventsAsync()).ShouldHaveSingleItem();
        (audit.Event, audit.AccountId, audit.Actor, audit.Detail).ShouldBe((AccountEventType.BasketCreated, null, ApiFactory.OwnerEmail, "Created basket 'Watchlist' (watch)"));
    }

    [Fact]
    public async Task Colour_and_purpose_have_defaults_and_the_description_may_be_left_out()
    {
        using var response = await Client.PostJsonAsync("/api/baskets", new { name = "Plain" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.ReadAsCreatedAsync<BasketView>(Ct);
        (created.Colour, created.Purpose, created.Description).ShouldBe((Basket.DefaultColour, "trading", null));
    }

    [Fact]
    public async Task Invalid_attributes_are_a_400_with_the_code_and_a_message_per_field_and_nothing_is_created()
    {
        using var response = await Client.PostJsonAsync("/api/baskets", new { name = " ", colour = "red", purpose = "other", description = new string('x', 501) }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (code, errors) = await ProblemAsync(response);
        code.ShouldBe("baskets.invalid");
        errors.Keys.Order().ShouldBe(["colour", "description", "name", "purpose"]);
        (await ListBasketsAsync()).Count.ShouldBe(3);
        (await BasketEventsAsync()).ShouldBeEmpty();
        using var noBody = await Client.PostAsync("/api/baskets", Ct);
        noBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemAsync(noBody)).Code.ShouldBe("baskets.invalid");
    }

    [Fact]
    public async Task A_name_of_an_active_basket_is_taken_ignoring_case_and_the_name_of_an_archived_one_is_free()
    {
        using var duplicate = await Client.PostJsonAsync("/api/baskets", new { name = "mychoice" }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var (code, errors) = await ProblemAsync(duplicate);
        (code, errors.Keys.Single()).ShouldBe(("baskets.name_taken", "name"));

        var best = await BasketAsync("Best stocks");
        using var archived = await SendAsync(HttpMethod.Delete, $"/api/baskets/{best.Id}");
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await CreateBasketAsync("best STOCKS");
        again.Id.ShouldNotBe(best.Id);
        (await ListBasketsAsync()).Select(b => b.Name).ShouldBe(["MyChoice", "MyLongTerm", "best STOCKS"]);
    }

    [Fact]
    public async Task Concurrent_creates_with_the_same_name_make_one_basket_and_the_others_are_told_the_name_is_taken()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Client.PostJsonAsync("/api/baskets", new { name = "Race" }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            (await ProblemAsync(conflict)).Code.ShouldBe("baskets.name_taken");
        }

        (await ListBasketsAsync()).Count(b => b.Name == "Race").ShouldBe(1);
    }

    [Fact]
    public async Task A_name_that_only_the_database_folds_to_an_existing_one_is_reported_as_taken_and_not_as_a_conflict()
    {
        // The Kelvin sign lowercases to k in PostgreSQL's lower() (the unique index) but is not equal to it ignoring case in .NET.
        var folds = await Factory.WithDbAsync(db => db.Database.SqlQuery<bool>($"SELECT lower(chr(8490)) = 'k' AS \"Value\"").SingleAsync(Ct));
        if (!folds)
        {
            Assert.Skip("This database's lower() does not fold the Kelvin sign.");
        }

        await CreateBasketAsync("kx");
        var other = await CreateBasketAsync("Other");

        using var create = await Client.PostJsonAsync("/api/baskets", new { name = "\u212Ax" }, Ct);
        using var rename = await Client.PutJsonAsync($"/api/baskets/{other.Id}", new { name = "\u212Ax", colour = other.Colour, purpose = other.Purpose }, Ct);

        foreach (var response in new[] { create, rename })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemAsync(response)).Code.ShouldBe("baskets.name_taken");
        }
    }

    [Fact]
    public async Task Updating_replaces_the_attributes_keeps_the_members_and_audits_a_rename_and_other_changes()
    {
        await ImportAsync();
        var choice = await BasketAsync("MyChoice");
        var tcs = await FindAsync("NSE:TCS");
        await AddAsync(choice.Id, tcs.Id);

        using var response = await Client.PutJsonAsync($"/api/baskets/{choice.Id}", new { name = "My choice", colour = "#00FF00", purpose = "watch", description = "Updated" }, Ct);

        var updated = await response.ReadAsync<BasketView>(Ct);
        (updated.Id, updated.Name, updated.Colour, updated.Purpose, updated.Description, updated.Position, updated.MemberCount)
            .ShouldBe((choice.Id, "My choice", "#00ff00", "watch", "Updated", 0, 1));
        (await BasketEventsAsync()).Select(e => (e.Event, e.Detail)).ShouldBe(
        [
            (AccountEventType.BasketMembersAdded, "Added 1 of 1 instruments to basket 'MyChoice' (0 were members already, 1 tracked now)"),
            (AccountEventType.BasketRenamed, "Renamed basket 'MyChoice' to 'My choice'"),
            (AccountEventType.BasketUpdated, "Changed the colour, purpose, description of basket 'My choice'"),
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task Updating_with_the_same_values_is_a_no_op_and_taking_another_baskets_name_is_a_conflict()
    {
        var choice = await BasketAsync("MyChoice");

        using var same = await Client.PutJsonAsync($"/api/baskets/{choice.Id}", new { name = choice.Name, colour = choice.Colour, purpose = choice.Purpose }, Ct);
        using var taken = await Client.PutJsonAsync($"/api/baskets/{choice.Id}", new { name = "MYLONGTERM", colour = choice.Colour, purpose = choice.Purpose }, Ct);

        (await same.ReadAsync<BasketView>(Ct)).ShouldBe(choice);
        taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(taken)).Code.ShouldBe("baskets.name_taken");
        (await BasketEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Archiving_hides_the_basket_keeps_its_history_and_instruments_stay_tracked()
    {
        await ImportAsync();
        var choice = await BasketAsync("MyChoice");
        var tcs = await FindAsync("NSE:TCS");
        await AddAsync(choice.Id, tcs.Id);

        using var archived = await SendAsync(HttpMethod.Delete, $"/api/baskets/{choice.Id}");

        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListBasketsAsync()).Select(b => b.Name).ShouldBe(["MyLongTerm", "Best stocks"]);
        var all = await ListBasketsAsync(includeArchived: true);
        all.Select(b => b.Name).ShouldBe(["MyLongTerm", "Best stocks", "MyChoice"], "archived baskets come last");
        all.Last().ArchivedAt.ShouldNotBeNull().ShouldBe(Now, Tolerance);
        var members = await MembersAsync(choice.Id);
        members.ArchivedAt.ShouldBe(all.Last().ArchivedAt);
        members.Items.Select(m => m.Instrument.Symbol).ShouldBe(["NSE:TCS"]);
        (await HistoryAsync(choice.Id)).Items.ShouldHaveSingleItem().Kind.ShouldBe("added");
        (await FindAsync("NSE:TCS")).Tracked.Active.ShouldBeTrue();
        (await BasketsOfAsync(tcs.Id)).ShouldBeEmpty("an archived basket no longer holds the instrument");
        (await BasketEventsAsync()).Single(e => e.Event == AccountEventType.BasketArchived).Detail.ShouldBe("Archived basket 'MyChoice' (1 instruments)");
    }

    [Fact]
    public async Task Archiving_twice_is_fine_and_an_archived_basket_rejects_every_change_with_a_code()
    {
        await ImportAsync();
        var choice = await BasketAsync("MyChoice");
        var other = await BasketAsync("MyLongTerm");
        var tcs = await FindAsync("NSE:TCS");
        await AddAsync(choice.Id, tcs.Id);
        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{choice.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SendAsync(HttpMethod.Delete, $"/api/baskets/{choice.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var attempts = new (HttpMethod Method, string Path, object Body)[]
        {
            (HttpMethod.Put, $"/api/baskets/{choice.Id}", new { name = "New" }),
            (HttpMethod.Post, $"/api/baskets/{choice.Id}/members", new { instrumentIds = new[] { tcs.Id } }),
            (HttpMethod.Delete, $"/api/baskets/{choice.Id}/members", new { instrumentIds = new[] { tcs.Id } }),
            (HttpMethod.Put, $"/api/baskets/{choice.Id}/members/order", new { instrumentIds = new[] { tcs.Id } }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = choice.Id, toBasketId = other.Id, instrumentIds = new[] { tcs.Id }, mode = "copy" }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = other.Id, toBasketId = choice.Id, instrumentIds = new[] { tcs.Id }, mode = "copy" }),
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await SendAsync(method, path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{method} {path}");
            (await ProblemAsync(response)).Code.ShouldBe("baskets.archived");
        }

        (await MemberIdsAsync(choice.Id)).ShouldBe([tcs.Id]);
        (await BasketEventsAsync()).Count(e => e.Event == AccountEventType.BasketArchived).ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_basket_is_a_404_with_a_code_on_every_route()
    {
        var id = Guid.NewGuid();
        var someone = Guid.NewGuid();

        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Put, $"/api/baskets/{id}", new { name = "New" }),
            (HttpMethod.Delete, $"/api/baskets/{id}", null),
            (HttpMethod.Get, $"/api/baskets/{id}/members", null),
            (HttpMethod.Get, $"/api/baskets/{id}/history", null),
            (HttpMethod.Post, $"/api/baskets/{id}/members", new { instrumentIds = new[] { someone } }),
            (HttpMethod.Delete, $"/api/baskets/{id}/members", new { instrumentIds = new[] { someone } }),
            (HttpMethod.Put, $"/api/baskets/{id}/members/order", new { instrumentIds = Array.Empty<Guid>() }),
            (HttpMethod.Post, "/api/baskets/transfer", new { fromBasketId = id, toBasketId = Guid.NewGuid(), instrumentIds = new[] { someone }, mode = "move" }),
        };
        foreach (var (method, path, body) in attempts)
        {
            using var response = await SendAsync(method, path, body);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}");
            (await ProblemAsync(response)).Code.ShouldBe("baskets.not_found");
        }
    }

    [Fact]
    public async Task Reordering_baskets_renumbers_them_is_idempotent_and_is_audited_once()
    {
        var order = (await ListBasketsAsync()).Select(b => b.Id).Reverse().ToArray();

        using var first = await Client.PutJsonAsync("/api/baskets/order", new { ids = order }, Ct);
        using var second = await Client.PutJsonAsync("/api/baskets/order", new { ids = order }, Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await ListBasketsAsync();
        after.Select(b => b.Id).ShouldBe(order);
        after.Select(b => b.Position).ShouldBe([0, 1, 2]);
        var audit = (await BasketEventsAsync()).ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketsReordered, "Reordered 3 baskets (2 moved)"));
    }

    [Fact]
    public async Task A_basket_order_that_is_not_exactly_the_active_baskets_is_a_400_and_changes_nothing()
    {
        var ids = (await ListBasketsAsync()).Select(b => b.Id).ToArray();
        var best = await BasketAsync("Best stocks");
        await SendAsync(HttpMethod.Delete, $"/api/baskets/{best.Id}");

        foreach (var body in new object[]
        {
            new { ids = ids[..1] },
            new { ids },
            new { ids = new[] { ids[0], ids[0] } },
            new { ids = new[] { ids[0], ids[1], Guid.NewGuid() } },
            new { ids = new[] { ids[0], ids[1], Guid.Empty } },
            new { },
        })
        {
            using var response = await Client.PutJsonAsync("/api/baskets/order", body, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var (code, errors) = await ProblemAsync(response);
            (code, errors.ContainsKey("ids")).ShouldBe(("baskets.invalid", true));
        }

        (await ListBasketsAsync()).Select(b => b.Id).ShouldBe(ids[..2]);
        using var noBody = await SendAsync(HttpMethod.Put, "/api/baskets/order");
        noBody.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_literal_routes_do_not_collide_with_the_basket_by_id_routes()
    {
        var choice = await BasketAsync("MyChoice");

        using var getOrder = await Client.GetAsync("/api/baskets/order", Ct);
        using var getTransfer = await Client.GetAsync("/api/baskets/transfer", Ct);
        using var postById = await Client.PostJsonAsync($"/api/baskets/{choice.Id}", new { name = "x" }, Ct);
        using var instrumentById = await Client.GetAsync($"/api/instruments/{Guid.NewGuid()}", Ct);
        using var instrumentBaskets = await Client.GetAsync($"/api/instruments/{Guid.NewGuid()}/baskets", Ct);

        getOrder.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        getTransfer.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        postById.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        instrumentById.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemAsync(instrumentBaskets)).Code.ShouldBe("instruments.not_found");
    }

    [Fact]
    public async Task Basket_changes_are_audited_with_names_and_counts_but_never_symbols_or_ids()
    {
        await ImportAsync();
        var choice = await BasketAsync("MyChoice");
        var tcs = await FindAsync("NSE:TCS");
        var reliance = await FindAsync("NSE:RELIANCE");
        await AddAsync(choice.Id, tcs.Id, reliance.Id);
        await RemoveAsync(choice.Id, tcs.Id);

        var details = (await BasketEventsAsync()).Select(e => e.Detail!).ToList();

        details.Count.ShouldBe(2);
        details.ShouldAllBe(d => !d.Contains("TCS") && !d.Contains("RELIANCE") && !d.Contains(tcs.Id.ToString()));
        (await BasketEventsAsync()).ShouldAllBe(e => e.AccountId == null && e.Actor == ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task The_basket_operations_are_listed_in_the_contract()
    {
        using var contract = await Client.GetAsync("/api/openapi/v1.json", Ct);
        var json = await contract.Content.ReadAsStringAsync(Ct);

        foreach (var operation in new[]
        {
            "ListBaskets", "CreateBasket", "UpdateBasket", "ArchiveBasket", "ReorderBaskets", "ListBasketMembers", "AddBasketMembers",
            "RemoveBasketMembers", "ReorderBasketMembers", "TransferBasketMembers", "GetBasketHistory", "ListInstrumentBaskets",
        })
        {
            json.ShouldContain($"\"operationId\": \"{operation}\"");
        }
    }
}
