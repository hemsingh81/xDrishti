using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research.CostSheets;

/// <summary>Criteria 7 and 8 of P3-03f at the handler: the replace policy, the descriptive update, the refusals and the concurrent write, against a fake store and a fake unit of work.</summary>
public sealed class LoadShowCostSheetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 5, 0, 0, TimeSpan.Zero);

    private readonly FakeCostSheetStore _store = new();

    private readonly IPlatformUnitOfWork _unit = Substitute.For<IPlatformUnitOfWork>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoadShowCostSheetTests()
    {
        _unit.TrySaveAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private LoadCostSheetHandler Loader => new(_store, _unit, new FakeTimeProvider(Now));

    private ValueTask<Result<LoadedCostSheet>> LoadAsync(string text, bool replace = false) => Loader.HandleAsync(new LoadCostSheetCommand(text, replace, "cli:cost-sheet"), Ct);

    /// <summary>The fixture sheet's text with another rate for the second schedule's delivery STT sell (the rates hash changes).</summary>
    private static string OtherRates() => FixtureSheet.Json.Replace("\"stt_sell_pct\":0.09", "\"stt_sell_pct\":0.095", StringComparison.Ordinal);

    [Fact]
    public async Task A_valid_sheet_is_stored_with_the_actor_and_the_instant_and_reported_unchecked()
    {
        var result = await LoadAsync(FixtureSheet.Json);

        var loaded = result.Value;
        (loaded.Id, loaded.Status, loaded.IsChecked, loaded.RatesSha256, loaded.Label).ShouldBe((FixtureSheet.Id, LoadStatus.Created, false, FixtureSheet.Sheet.RatesSha256, FixtureSheet.Sheet.Label));
        var save = _store.Saves.ShouldHaveSingleItem();
        (save.Sheet, save.Actor, save.Now).ShouldBe((FixtureSheet.Sheet, "cli:cost-sheet", Now));
        await _unit.Received(1).TrySaveAsync(Ct);
    }

    [Fact]
    public async Task A_file_with_three_problems_lists_them_all_and_stores_nothing()
    {
        var broken = FixtureSheet.Json.Replace("\"gst_pct\":18", "\"gst_pct\":-1", StringComparison.Ordinal).Replace("\"label\":\"FIXTURE sheet (invented numbers)\",", string.Empty, StringComparison.Ordinal).Replace("\"rounding\":\"half_away_from_zero\"", "\"rounding\":\"sideways\"", StringComparison.Ordinal);

        var result = await LoadAsync(broken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields.ShouldNotBeNull().Count.ShouldBeGreaterThanOrEqualTo(3);
        _store.Saves.ShouldBeEmpty();
        await _unit.DidNotReceive().TrySaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_test_id_a_repeated_property_name_and_text_the_parser_refuses_store_nothing()
    {
        var test = await LoadAsync(TestCostSheet.Json);
        var repeated = await LoadAsync(FixtureSheet.Json.Replace("\"id\":\"fixture-a\"", "\"id\":\"fixture-a\",\"id\":\"fixture-b\"", StringComparison.Ordinal));
        var garbage = await LoadAsync("not json");
        var empty = await LoadAsync(string.Empty);

        test.Error.Code.ShouldBe(CostErrors.TestSheetNotStoredCode);
        repeated.Error.Fields.ShouldNotBeNull().ShouldContain(f => f.Code == CostErrors.DuplicateProperty);
        garbage.IsFailure.ShouldBeTrue();
        empty.IsFailure.ShouldBeTrue();
        _store.Saves.ShouldBeEmpty();
    }

    [Fact]
    public async Task Other_rates_under_an_existing_id_need_replace_and_the_refusal_names_both_hashes()
    {
        _store.With(FixtureSheet.Sheet);
        var other = CostSheetJson.Parse(OtherRates()).Value;

        var refused = await LoadAsync(OtherRates());
        var replaced = await LoadAsync(OtherRates(), replace: true);

        refused.Error.Code.ShouldBe(CostErrors.ReplaceRequiredCode);
        refused.Error.Message.ShouldContain(FixtureSheet.Sheet.RatesSha256);
        refused.Error.Message.ShouldContain(other.RatesSha256);
        replaced.Value.Status.ShouldBe(LoadStatus.Replaced);
        replaced.Value.PreviousRatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        _store.Saves.ShouldHaveSingleItem().Sheet.RatesSha256.ShouldBe(other.RatesSha256);
    }

    [Fact]
    public async Task The_same_rates_with_another_checked_note_update_the_description_without_replace_and_the_hash_is_unchanged()
    {
        _store.With(FixtureSheet.Sheet);
        var noted = FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: 3 delivery rows within 0.05");

        var result = await LoadAsync(noted.ToCanonicalJson());

        result.Value.Status.ShouldBe(LoadStatus.DescriptionUpdated);
        result.Value.IsChecked.ShouldBeTrue();
        result.Value.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        _store.Saves.ShouldHaveSingleItem().Sheet.Checked.ShouldBe("check 2026-10-15: 3 delivery rows within 0.05");
    }

    [Fact]
    public async Task Loading_the_same_file_twice_changes_nothing()
    {
        await LoadAsync(FixtureSheet.Json);

        var again = await LoadAsync(FixtureSheet.Json);

        again.Value.Status.ShouldBe(LoadStatus.Unchanged);
        _store.Saves.Count.ShouldBe(1);
        await _unit.Received(1).TrySaveAsync(Ct);
    }

    [Fact]
    public async Task A_stored_text_that_does_not_parse_is_overwritten_only_with_replace()
    {
        _store.Failure = CostErrors.SheetInvalid("schedules", CostErrors.Required, "is required.");

        var refused = await LoadAsync(FixtureSheet.Json);
        var replaced = await LoadAsync(FixtureSheet.Json, replace: true);

        refused.Error.Code.ShouldBe(CostErrors.ReplaceRequiredCode);
        replaced.Value.Status.ShouldBe(LoadStatus.Replaced);
    }

    [Fact]
    public async Task A_concurrent_write_is_a_conflict_and_reports_nothing_as_stored()
    {
        _unit.TrySaveAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await LoadAsync(FixtureSheet.Json);

        result.Error.Code.ShouldBe(CostErrors.ConcurrentWriteCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void Nothing_is_audited_the_audit_trail_is_the_authentication_trail_and_has_no_member_for_a_settings_write()
    {
        Enum.GetNames<AuditEventType>().ShouldBe(
        [
            "LoginSucceeded", "LoginFailed", "AccountLocked", "LoginRefusedLocked", "Logout", "SessionExpiredIdle", "SessionExpiredAbsolute", "OwnerProvisioned", "OwnerPasswordReset", "StepUpSucceeded", "StepUpFailed",
            "PasswordChanged", "ProfileUpdated",
        ]);
        typeof(LoadCostSheetHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ShouldNotContain("IAuditLog");
    }

    [Fact]
    public async Task Show_gives_the_canonical_text_the_hash_and_the_label_and_an_unknown_id_is_not_found()
    {
        _store.With(FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: ok"));
        var handler = new ShowCostSheetHandler(_store);

        var shown = await handler.HandleAsync(new ShowCostSheetQuery(FixtureSheet.Id), Ct);
        var unknown = await handler.HandleAsync(new ShowCostSheetQuery("nope"), Ct);

        shown.Value.CanonicalJson.ShouldBe(FixtureSheet.Build(FixtureSheet.Id, "check 2026-10-15: ok").ToCanonicalJson());
        shown.Value.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
        shown.Value.Label.ShouldBe("costs: sheet fixture-a");
        unknown.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Show_of_an_unchecked_sheet_says_so()
    {
        _store.With(FixtureSheet.Sheet);

        var shown = await new ShowCostSheetHandler(_store).HandleAsync(new ShowCostSheetQuery(FixtureSheet.Id), Ct);

        shown.Value.Label.ShouldBe("costs: sheet fixture-a (not checked against bills)");
    }
}
