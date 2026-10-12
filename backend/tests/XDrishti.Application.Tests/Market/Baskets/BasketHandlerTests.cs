using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

public sealed class BasketHandlerTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    private Task<Result<BasketView>> CreateAsync(string? name, string? colour = null, string? purpose = null, string? description = null) =>
        _f.Create.HandleAsync(new CreateBasketCommand(new BasketInput(name, colour, purpose, description), BasketFixture.Owner), Ct).AsTask();

    private Task<Result<BasketView>> UpdateAsync(Basket basket, string? name, string? colour = null, string? purpose = null, string? description = null) =>
        _f.Update.HandleAsync(new UpdateBasketCommand(basket.Id, new BasketInput(name, colour, purpose, description), BasketFixture.Owner), Ct).AsTask();

    private Task<Result> ReorderAsync(params Guid[] ids) =>
        _f.ReorderBaskets.HandleAsync(new ReorderBasketsCommand(new BasketOrderInput(ids), BasketFixture.Owner), Ct).AsTask();

    [Fact]
    public async Task A_new_basket_is_appended_empty_and_the_creation_is_audited_with_its_name()
    {
        _f.Basket("First");

        var result = await CreateAsync("  MyChoice ", "#ABCDEF", "long_term", "notes");

        result.IsSuccess.ShouldBeTrue();
        var view = result.Value;
        (view.Name, view.Colour, view.Purpose, view.Description, view.Position, view.MemberCount, view.ArchivedAt)
            .ShouldBe(("MyChoice", "#abcdef", "long_term", "notes", 1, 0, null));
        view.CreatedAt.ShouldBe(_f.Clock.GetUtcNow());
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.AccountId, audit.Actor, audit.Detail).ShouldBe((AccountEventType.BasketCreated, null, BasketFixture.Owner, "Created basket 'MyChoice' (long_term)"));
    }

    [Fact]
    public async Task Invalid_attributes_are_rejected_with_the_field_names_and_nothing_is_saved()
    {
        var result = await CreateAsync("", "red", "x");

        result.Error.Code.ShouldBe("baskets.invalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!.Select(f => f.Field).ShouldBe(["name", "colour", "purpose"]);
        _f.Data.Baskets.ShouldBeEmpty();
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_used_by_an_active_basket_is_taken_ignoring_case()
    {
        _f.Basket("MyChoice");

        var result = await CreateAsync("mychoice");

        result.Error.Code.ShouldBe("baskets.name_taken");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Fields!.Single().Field.ShouldBe("name");
        _f.Data.Baskets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_name_of_an_archived_basket_is_free_again()
    {
        var old = _f.Basket("MyChoice");
        old.Archive(_f.Clock.GetUtcNow());

        var result = await CreateAsync("MyChoice");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_create_that_loses_a_race_for_the_name_reads_again_and_reports_the_name_as_taken()
    {
        _f.Data.Collisions.Enqueue(() => _f.Basket("Core"));

        var result = await CreateAsync("Core");

        result.Error.Code.ShouldBe("baskets.name_taken");
        _f.Data.Baskets.Count.ShouldBe(1);
        _f.Data.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task A_create_refused_because_the_database_counts_the_name_as_taken_reports_the_name_not_a_conflict()
    {
        _f.Data.Collisions.Enqueue(() => _f.Data.NamesTakenInDatabase.Add("Kx")); // equal to another name only for the database's lower()

        var result = await CreateAsync("Kx");

        result.Error.Code.ShouldBe("baskets.name_taken");
        _f.Data.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task An_update_refused_because_the_database_counts_the_new_name_as_taken_reports_the_name_not_a_conflict()
    {
        var core = _f.Basket("Core");
        _f.Data.Collisions.Enqueue(() => _f.Data.NamesTakenInDatabase.Add("Kx"));

        var result = await UpdateAsync(core, "Kx");

        result.Error.Code.ShouldBe("baskets.name_taken");
        _f.Data.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Repeated_collisions_end_in_a_conflict_instead_of_looping()
    {
        for (var i = 0; i < BasketChecks.Attempts + 2; i++)
        {
            _f.Data.Collisions.Enqueue(() => { });
        }

        var result = await CreateAsync("Core");

        result.Error.Code.ShouldBe("baskets.conflict");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _f.Data.Saves.ShouldBe(BasketChecks.Attempts);
    }

    [Fact]
    public async Task Updating_replaces_the_attributes_and_audits_a_rename_and_other_changes_separately()
    {
        var basket = _f.Basket("Old");

        var result = await UpdateAsync(basket, "New", "#00ff00", "watch", "d");

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Name, result.Value.Colour, result.Value.Purpose, result.Value.Description).ShouldBe(("New", "#00ff00", "watch", "d"));
        _f.Master.Events.Select(e => (e.Event, e.Detail)).ShouldBe(
        [
            (AccountEventType.BasketRenamed, "Renamed basket 'Old' to 'New'"),
            (AccountEventType.BasketUpdated, "Changed the colour, purpose, description of basket 'New'"),
        ]);
    }

    [Fact]
    public async Task Updating_without_a_difference_saves_and_audits_nothing()
    {
        var basket = _f.Basket("Core");

        var result = await UpdateAsync(basket, "Core", Basket.DefaultColour, "trading", null);

        result.IsSuccess.ShouldBeTrue();
        _f.Data.Saves.ShouldBe(0);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_basket_can_keep_its_own_name_with_another_case_but_not_take_the_name_of_another()
    {
        var core = _f.Basket("Core");
        _f.Basket("Other");

        (await UpdateAsync(core, "CORE")).IsSuccess.ShouldBeTrue();
        (await UpdateAsync(core, "other")).Error.Code.ShouldBe("baskets.name_taken");
        core.Name.ShouldBe("CORE");
    }

    [Fact]
    public async Task An_unknown_basket_is_not_found_and_an_archived_one_cannot_be_changed()
    {
        var gone = _f.Basket("Gone");
        gone.Archive(_f.Clock.GetUtcNow());

        (await _f.Update.HandleAsync(new UpdateBasketCommand(Guid.NewGuid(), new BasketInput("X", null, null, null), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
        var archived = await UpdateAsync(gone, "Renamed");
        archived.Error.Code.ShouldBe("baskets.archived");
        archived.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Archiving_hides_the_basket_keeps_its_members_and_is_audited_with_the_count()
    {
        var basket = _f.Basket("Core");
        _f.Data.AddMember(basket, _f.Instruments("NSE:AAA")[0], _f.Clock.GetUtcNow());

        var result = await _f.Archive.HandleAsync(new ArchiveBasketCommand(basket.Id, BasketFixture.Owner), Ct);

        result.IsSuccess.ShouldBeTrue();
        (basket.IsArchived, basket.ArchivedAt).ShouldBe((true, _f.Clock.GetUtcNow()));
        _f.Data.Members.ShouldHaveSingleItem().IsActive.ShouldBeTrue();
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketArchived, "Archived basket 'Core' (1 instruments)"));
    }

    [Fact]
    public async Task Archiving_twice_changes_and_audits_nothing_the_second_time()
    {
        var basket = _f.Basket("Core");
        await _f.Archive.HandleAsync(new ArchiveBasketCommand(basket.Id, BasketFixture.Owner), Ct);
        var archivedAt = basket.ArchivedAt;
        _f.Clock.Advance(TimeSpan.FromDays(1));

        var again = await _f.Archive.HandleAsync(new ArchiveBasketCommand(basket.Id, BasketFixture.Owner), Ct);

        again.IsSuccess.ShouldBeTrue();
        basket.ArchivedAt.ShouldBe(archivedAt);
        _f.Master.Events.Count.ShouldBe(1);
        (await _f.Archive.HandleAsync(new ArchiveBasketCommand(Guid.NewGuid(), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
    }

    [Fact]
    public async Task Reordering_numbers_the_active_baskets_from_zero_in_the_given_order_and_audits_the_count()
    {
        var a = _f.Basket("A");
        var b = _f.Basket("B");
        var c = _f.Basket("C");

        var result = await ReorderAsync(c.Id, a.Id, b.Id);

        result.IsSuccess.ShouldBeTrue();
        (c.Position, a.Position, b.Position).ShouldBe((0, 1, 2));
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketsReordered, "Reordered 3 baskets (3 moved)"));
    }

    [Fact]
    public async Task Reordering_to_the_current_order_is_a_no_op_that_saves_and_audits_nothing()
    {
        var a = _f.Basket("A");
        var b = _f.Basket("B");

        var result = await ReorderAsync(a.Id, b.Id);

        result.IsSuccess.ShouldBeTrue();
        _f.Data.Saves.ShouldBe(0);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reorder_list_that_is_not_exactly_the_active_set_is_rejected_and_changes_nothing()
    {
        var a = _f.Basket("A");
        var b = _f.Basket("B");
        var archived = _f.Basket("Z");
        archived.Archive(_f.Clock.GetUtcNow());

        foreach (var ids in new[] { new[] { a.Id }, [b.Id, a.Id, Guid.NewGuid()], [b.Id, a.Id, archived.Id], [a.Id, a.Id], [b.Id, Guid.Empty] })
        {
            var result = await ReorderAsync(ids);

            result.Error.Code.ShouldBe("baskets.invalid");
            result.Error.Fields!.Single().Field.ShouldBe("ids");
        }

        (await _f.ReorderBaskets.HandleAsync(new ReorderBasketsCommand(new BasketOrderInput(null), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.invalid");
        (a.Position, b.Position).ShouldBe((0, 1));
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_reorder_whose_basket_list_changed_between_two_tries_is_a_conflict_not_a_bad_request()
    {
        var a = _f.Basket("A");
        var b = _f.Basket("B");
        _f.Data.Collisions.Enqueue(() => _f.Basket("C"));

        var result = await ReorderAsync(b.Id, a.Id);

        result.Error.Code.ShouldBe("baskets.conflict");
    }
}
