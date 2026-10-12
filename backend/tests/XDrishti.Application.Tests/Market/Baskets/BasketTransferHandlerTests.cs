using XDrishti.Application.Market.Baskets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Tests.Market.Baskets;

public sealed class BasketTransferHandlerTests
{
    private readonly BasketFixture _f = new();

    private static CancellationToken Ct => BasketFixture.Ct;

    private async Task<(Basket From, Basket To, Guid[] Ids)> SeedAsync(int count = 3)
    {
        var from = _f.Basket("From");
        var to = _f.Basket("To");
        var ids = _f.Instruments([.. Enumerable.Range(0, count).Select(i => $"NSE:S{i}")]);
        await _f.AddOkAsync(from, ids);
        _f.Master.Events.Clear();
        return (from, to, ids);
    }

    [Fact]
    public async Task Copy_adds_the_instruments_to_the_target_in_the_source_order_and_leaves_the_source_alone()
    {
        var (from, to, ids) = await SeedAsync();
        _f.Data.Members.First(m => m.InstrumentId == ids[0]).MoveTo(5);

        var result = await _f.TransferAsync(from, to, "copy", ids[1], ids[0]);

        result.Value.ShouldBe(new TransferResult("copy", 2, 2, 0, 0, 0, 0, 3, 2));
        _f.Data.ActiveInstruments(to).ShouldBe([ids[1], ids[0]], "the source's order, not the order of the request");
        _f.Data.ActiveInstruments(from).Count.ShouldBe(3);
        var audit = _f.Master.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Detail).ShouldBe((AccountEventType.BasketMembersTransferred, "Copied 2 of 2 instruments from basket 'From' to basket 'To' (0 already there, 0 not in the source, 0 tracked now)"));
    }

    [Fact]
    public async Task Move_adds_to_the_target_and_ends_the_stay_in_the_source_in_one_save()
    {
        var (from, to, ids) = await SeedAsync();
        var saves = _f.Data.Saves;
        _f.Clock.Advance(TimeSpan.FromDays(1));

        var result = await _f.TransferAsync(from, to, "move", ids[0], ids[2]);

        result.Value.ShouldBe(new TransferResult("move", 2, 2, 0, 2, 0, 0, 1, 2));
        _f.Data.ActiveInstruments(from).ShouldBe([ids[1]]);
        _f.Data.ActiveInstruments(to).ShouldBe([ids[0], ids[2]]);
        _f.Data.Members.Where(m => m.BasketId == from.Id && !m.IsActive).ShouldAllBe(m => m.ValidTo == _f.Clock.GetUtcNow());
        _f.Data.Saves.ShouldBe(saves + 1);
        _f.Master.Events.ShouldHaveSingleItem().Detail.ShouldStartWith("Moved 2 of 2 instruments");
        (from.Revision, to.Revision).ShouldBe((2, 1));
    }

    [Fact]
    public async Task The_note_of_a_member_goes_with_it()
    {
        var from = _f.Basket("From");
        var to = _f.Basket("To");
        var id = _f.Instruments("NSE:AAA")[0];
        await _f.Add.HandleAsync(new AddBasketMembersCommand(from.Id, new AddMembersInput([id], "why"), BasketFixture.Owner), Ct);

        await _f.TransferAsync(from, to, "copy", id);

        _f.Data.Members.Single(m => m.BasketId == to.Id).Note.ShouldBe("why");
    }

    [Fact]
    public async Task Repeating_a_move_changes_nothing_and_reports_the_instruments_as_not_in_the_source()
    {
        var (from, to, ids) = await SeedAsync();
        await _f.TransferAsync(from, to, "move", ids);
        _f.Master.Events.Clear();
        var saves = _f.Data.Saves;

        var again = await _f.TransferAsync(from, to, "move", ids);

        again.Value.ShouldBe(new TransferResult("move", 3, 0, 0, 0, 3, 0, 0, 3));
        _f.Data.Saves.ShouldBe(saves);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeating_a_copy_changes_nothing_and_reports_the_instruments_as_already_there()
    {
        var (from, to, ids) = await SeedAsync();
        await _f.TransferAsync(from, to, "copy", ids);
        _f.Master.Events.Clear();
        var saves = _f.Data.Saves;

        var again = await _f.TransferAsync(from, to, "copy", ids);

        again.Value.ShouldBe(new TransferResult("copy", 3, 0, 3, 0, 0, 0, 3, 3));
        _f.Data.Saves.ShouldBe(saves);
        _f.Master.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Moving_an_instrument_that_the_target_has_already_only_removes_it_from_the_source()
    {
        var (from, to, ids) = await SeedAsync(2);
        await _f.AddOkAsync(to, ids[0]);

        var result = await _f.TransferAsync(from, to, "move", ids);

        result.Value.ShouldBe(new TransferResult("move", 2, 1, 1, 2, 0, 0, 0, 2));
        _f.Data.ActiveInstruments(to).Count.ShouldBe(2);
        _f.Data.ActiveInstruments(from).ShouldBeEmpty();
    }

    [Fact]
    public async Task Instruments_that_are_not_members_of_the_source_are_skipped()
    {
        var (from, to, ids) = await SeedAsync(2);
        var stranger = _f.Instruments("NSE:OTHER")[0];

        var result = await _f.TransferAsync(from, to, "copy", ids[0], stranger);

        result.Value.ShouldBe(new TransferResult("copy", 2, 1, 0, 0, 1, 0, 2, 1));
        _f.Data.ActiveInstruments(to).ShouldBe([ids[0]]);
    }

    [Fact]
    public async Task A_retired_instrument_that_is_not_tracked_refuses_the_whole_transfer()
    {
        var from = _f.Basket("From");
        var to = _f.Basket("To");
        var live = _f.Master.AddInstrument("NSE:AAA");
        var gone = _f.Master.AddInstrument("NSE:OLD", retired: true);
        _f.Data.AddMember(from, live.Id, _f.Clock.GetUtcNow());
        _f.Data.AddMember(from, gone.Id, _f.Clock.GetUtcNow());

        var result = await _f.TransferAsync(from, to, "move", live.Id, gone.Id);

        result.Error.Code.ShouldBe("instruments.retired");
        _f.Data.ActiveInstruments(to).ShouldBeEmpty();
        _f.Data.ActiveInstruments(from).Count.ShouldBe(2);
        _f.Data.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Instruments_that_are_not_tracked_are_tracked_in_the_same_save()
    {
        var (from, to, ids) = await SeedAsync(2);
        _f.Master.Tracked.Single(t => t.Id == ids[0]).Deactivate(_f.Clock.GetUtcNow());

        var result = await _f.TransferAsync(from, to, "copy", ids);

        result.Value.NewlyTracked.ShouldBe(1);
        _f.Master.Tracked.ShouldAllBe(t => t.Active);
    }

    [Fact]
    public async Task The_target_cannot_exceed_two_thousand_members()
    {
        var (from, to, ids) = await SeedAsync(2);
        for (var i = 0; i < BasketLimits.MaxActiveMembers - 1; i++)
        {
            _f.Data.AddMember(to, Guid.NewGuid(), _f.Clock.GetUtcNow());
        }

        var result = await _f.TransferAsync(from, to, "move", ids);

        result.Error.Code.ShouldBe("baskets.too_many_members");
        _f.Data.ActiveInstruments(from).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_transfer_that_collides_with_a_concurrent_change_reads_again()
    {
        var (from, to, ids) = await SeedAsync(2);
        _f.Data.Collisions.Enqueue(() => _f.Data.AddMember(to, ids[0], _f.Clock.GetUtcNow()));

        var result = await _f.TransferAsync(from, to, "copy", ids);

        result.Value.ShouldBe(new TransferResult("copy", 2, 1, 1, 0, 0, 0, 2, 2));
    }

    [Fact]
    public async Task Bad_requests_are_rejected_before_anything_is_read()
    {
        var (from, to, ids) = await SeedAsync(1);
        var archived = _f.Basket("Old");
        archived.Archive(_f.Clock.GetUtcNow());

        (await _f.TransferAsync(from, to, "swap", ids)).Error.Fields!.Single().Field.ShouldBe("mode");
        (await _f.TransferAsync(from, to, "", ids)).Error.Code.ShouldBe("baskets.invalid");
        (await _f.TransferAsync(from, from, "copy", ids)).Error.Fields!.Single().Field.ShouldBe("toBasketId");
        (await _f.TransferAsync(from, to, "copy")).Error.Fields!.Single().Field.ShouldBe("instrumentIds");
        (await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(Guid.Empty, to.Id, ids, "copy"), BasketFixture.Owner), Ct)).Error.Fields!.Single().Field.ShouldBe("fromBasketId");
        (await _f.Transfer.HandleAsync(new TransferBasketMembersCommand(new TransferInput(from.Id, Guid.NewGuid(), ids, "copy"), BasketFixture.Owner), Ct)).Error.Code.ShouldBe("baskets.not_found");
        (await _f.TransferAsync(from, archived, "copy", ids)).Error.Code.ShouldBe("baskets.archived");
        (await _f.TransferAsync(archived, to, "copy", ids)).Error.Code.ShouldBe("baskets.archived");
        _f.Data.ActiveInstruments(to).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_mode_is_not_case_sensitive()
    {
        var (from, to, ids) = await SeedAsync(1);

        var result = await _f.TransferAsync(from, to, " MOVE ", ids);

        result.Value.Mode.ShouldBe("move");
    }
}
