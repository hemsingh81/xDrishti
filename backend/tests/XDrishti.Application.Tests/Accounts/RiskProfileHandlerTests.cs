using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

public sealed class RiskProfileHandlerTests
{
    private readonly AccountsFixture _fx = new();

    private static RiskProfileInput Valid(string name = "Aggressive") => AccountDataTestValues.Standard with { Name = name, RiskPerTradePct = 1.0m };

    private async Task<RiskProfileView> CreatedProfileAsync(string name = "Aggressive") =>
        (await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(Valid(name), AccountsFixture.Actor), CancellationToken.None)).Value;

    [Fact]
    public async Task The_list_shows_each_profile_with_the_number_of_accounts_using_it()
    {
        await _fx.CreatedAsync(Inputs.Account(trading: Inputs.Trading(_fx.Standard.Id)));
        await _fx.CreatedAsync(Inputs.Account(name: "Example Two", trading: Inputs.Trading(_fx.Standard.Id)));
        var archived = await _fx.CreatedAsync(Inputs.Account(name: "Example Three", trading: Inputs.Trading(_fx.Conservative.Id)));
        archived.Archive(AccountsFixture.Start);

        var list = await _fx.ListProfiles().HandleAsync(new ListRiskProfilesQuery(), CancellationToken.None);

        list.Select(p => (p.Name, p.AccountCount, p.IsSeeded)).ShouldBe([("Conservative", 0, true), ("Standard", 2, true)]);
        list.Single(p => p.Name == "Conservative").RiskPerTradePct.ShouldBe(0.25m);
        list.Single(p => p.Name == "Standard").WeeklyLossAction.ShouldBe("halve_size");
    }

    [Fact]
    public async Task A_profile_is_created_and_audited()
    {
        var result = await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(Valid(), AccountsFixture.Actor), CancellationToken.None);

        result.Value.Name.ShouldBe("Aggressive");
        result.Value.RiskPerTradePct.ShouldBe(1.0m);
        result.Value.IsSeeded.ShouldBeFalse();
        var entry = _fx.Data.Events.ShouldHaveSingleItem();
        (entry.Event, entry.AccountId, entry.Actor).ShouldBe((AccountEventType.RiskProfileCreated, null, AccountsFixture.Actor));
    }

    [Fact]
    public async Task Missing_and_out_of_range_values_are_field_errors()
    {
        var missing = await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(new RiskProfileInput("Empty", null, null, null, null, null, null, null, null, null, null, null, null, null), AccountsFixture.Actor), CancellationToken.None);
        var range = await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(Valid() with { RiskPerTradePct = 3m, MaxOpenPositions = 0 }, AccountsFixture.Actor), CancellationToken.None);
        var action = await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(Valid() with { WeeklyLossAction = "panic" }, AccountsFixture.Actor), CancellationToken.None);

        missing.Error.Fields!.Count.ShouldBe(13);
        missing.Error.Fields!.ShouldAllBe(f => f.Message == "Required.");
        range.Error.Fields!.Select(f => f.Field).ShouldBe(["riskPerTradePct", "maxOpenPositions"], ignoreOrder: true);
        action.Error.Fields!.Single().Field.ShouldBe("weeklyLossAction");
        _fx.Data.Profiles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_duplicate_name_is_a_conflict_ignoring_case()
    {
        var result = await _fx.CreateProfile().HandleAsync(new CreateRiskProfileCommand(Valid("STANDARD"), AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
    }

    [Fact]
    public async Task An_update_replaces_the_values_and_the_audit_text_lists_what_changed()
    {
        var created = await CreatedProfileAsync();

        var result = await _fx.UpdateProfile().HandleAsync(
            new UpdateRiskProfileCommand(created.Id, Valid("Aggressive 2") with { MaxOpenPositions = 8, WeeklyLossAction = "alert_only" }, AccountsFixture.Actor), CancellationToken.None);

        result.Value.Name.ShouldBe("Aggressive 2");
        result.Value.MaxOpenPositions.ShouldBe(8);
        _fx.Data.Events.Last().Detail.ShouldBe("Risk profile 'Aggressive 2': name 'Aggressive' -> 'Aggressive 2'; maxOpenPositions 5 -> 8; weeklyLossAction halve_size -> alert_only");
    }

    [Fact]
    public async Task A_profile_can_keep_its_own_name_but_not_take_another_ones()
    {
        var created = await CreatedProfileAsync();

        var same = await _fx.UpdateProfile().HandleAsync(new UpdateRiskProfileCommand(created.Id, Valid(), AccountsFixture.Actor), CancellationToken.None);
        var clash = await _fx.UpdateProfile().HandleAsync(new UpdateRiskProfileCommand(created.Id, Valid("standard"), AccountsFixture.Actor), CancellationToken.None);

        same.IsSuccess.ShouldBeTrue();
        clash.Error.Code.ShouldBe(AccountErrors.ConflictCode);
    }

    [Fact]
    public async Task A_built_in_profile_can_be_edited()
    {
        var result = await _fx.UpdateProfile().HandleAsync(
            new UpdateRiskProfileCommand(_fx.Standard.Id, AccountDataTestValues.Standard with { Name = "Standard", MaxOpenPositions = 6 }, AccountsFixture.Actor), CancellationToken.None);

        result.Value.MaxOpenPositions.ShouldBe(6);
    }

    [Fact]
    public async Task Updating_or_deleting_an_unknown_profile_is_not_found()
    {
        var update = await _fx.UpdateProfile().HandleAsync(new UpdateRiskProfileCommand(Guid.NewGuid(), Valid(), AccountsFixture.Actor), CancellationToken.None);
        var delete = await _fx.DeleteProfile().HandleAsync(new DeleteRiskProfileCommand(Guid.NewGuid(), AccountsFixture.Actor), CancellationToken.None);

        update.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        delete.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task An_unused_profile_is_deleted_and_audited()
    {
        var created = await CreatedProfileAsync();

        var result = await _fx.DeleteProfile().HandleAsync(new DeleteRiskProfileCommand(created.Id, AccountsFixture.Actor), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.Profiles.ShouldNotContain(p => p.Id == created.Id);
        _fx.Data.Events.Last().Event.ShouldBe(AccountEventType.RiskProfileDeleted);
        _fx.Data.SerializedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task A_profile_in_use_cannot_be_deleted()
    {
        var created = await CreatedProfileAsync();
        await _fx.CreatedAsync(Inputs.Account(trading: Inputs.Trading(created.Id)));

        var result = await _fx.DeleteProfile().HandleAsync(new DeleteRiskProfileCommand(created.Id, AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        _fx.Data.Profiles.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task A_built_in_profile_cannot_be_deleted()
    {
        var result = await _fx.DeleteProfile().HandleAsync(new DeleteRiskProfileCommand(_fx.Standard.Id, AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
    }

    [Fact]
    public async Task A_collision_at_save_time_restores_a_deleted_profile()
    {
        var created = await CreatedProfileAsync();
        _fx.Data.CollidingSaves = 1;

        var result = await _fx.DeleteProfile().HandleAsync(new DeleteRiskProfileCommand(created.Id, AccountsFixture.Actor), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.ConcurrentChange);
        _fx.Data.Profiles.ShouldContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Buckets_are_listed_and_added()
    {
        var added = await _fx.AddBucket().HandleAsync(new AddBucketCommand(new BucketInput("  Growth  "), AccountsFixture.Actor), CancellationToken.None);
        var list = await _fx.ListBuckets().HandleAsync(new ListBucketsQuery(), CancellationToken.None);

        added.Value.Name.ShouldBe("Growth");
        list.Select(b => b.Name).ShouldBe(["Trading", "MyLongTerm", "MyChoice", "Growth"]);
        _fx.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AccountEventType.BucketAdded);
    }

    [Theory]
    [InlineData("trading")]
    [InlineData("MYCHOICE")]
    public async Task A_bucket_name_that_exists_ignoring_case_is_a_conflict(string name)
    {
        var result = await _fx.AddBucket().HandleAsync(new AddBucketCommand(new BucketInput(name), AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a/b")]
    public async Task An_invalid_bucket_name_is_a_field_error(string? name)
    {
        var result = await _fx.AddBucket().HandleAsync(new AddBucketCommand(new BucketInput(name), AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        result.Error.Fields!.Single().Field.ShouldBe("name");
    }
}
