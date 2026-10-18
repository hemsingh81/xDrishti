using NSubstitute;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Research;

/// <summary>
/// Criteria 1 and 2 of P3-03f: the cost choice is a type, a bad choice is <c>simulation.invalid</c> with one entry per field, an unknown or not yet effective sheet is refused after <c>Prepare</c> and before any other port, a gross choice reads
/// nothing, and one <see cref="CostContext"/> reaches every replay and the header.
/// </summary>
public sealed class RunSimulationCostsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Id = new("00000000-0000-0000-0000-0000000000a1");

    private static RunFixture WithSheet(out FakeCostSheetStore store)
    {
        var fixture = new RunFixture();
        store = fixture.CostSheets.With(FixtureSheet.Sheet);
        return fixture;
    }

    [Fact]
    public void A_gross_choice_has_no_capital_and_a_sheet_choice_always_has_an_id()
    {
        CostChoice.Gross.ShouldBeOfType<CostChoice.GrossChoice>();
        CostChoice.Gross.ShouldBeSameAs(CostChoice.Gross);
        var sheet = CostChoice.Sheet("fixture-a").ShouldBeOfType<CostChoice.SheetChoice>();
        (sheet.SheetId, sheet.Capital, sheet.Profile, sheet.MaxCostR).ShouldBe(("fixture-a", 1_000_000m, "standard", 0.10m));
        typeof(CostChoice.GrossChoice).GetProperties().Select(p => p.Name).ShouldNotContain("Capital");
        typeof(CostChoice).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).ShouldBeEmpty("the abstract record's constructor is private");
        typeof(CostChoice.SheetChoice).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).ShouldBeEmpty("only CostChoice.Sheet builds a sheet choice");
        typeof(CostChoice.GrossChoice).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).ShouldBeEmpty("only CostChoice.Gross builds a gross choice");
    }

    [Fact]
    public void The_request_takes_the_choice_as_a_required_parameter_after_the_basket_id()
    {
        var parameters = typeof(SimulationRequest).GetConstructors().Single(c => c.GetParameters().Length > 2).GetParameters();

        var costs = parameters.Single(p => p.Name == "Costs");
        costs.HasDefaultValue.ShouldBeFalse("a call site that forgets the choice does not compile");
        parameters[costs.Position - 1].Name.ShouldBe("BasketId");
        parameters.Skip(costs.Position + 1).ShouldAllBe(p => p.HasDefaultValue);
    }

    [Fact]
    public async Task A_request_without_a_choice_is_invalid_naming_costs_and_nothing_is_read()
    {
        var fixture = new RunFixture();

        var result = await fixture.RunAsync(RunFixture.List([Id]) with { Costs = null! }, Ct);

        result.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["costs"]);
        fixture.CostSheets.Finds.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }

    private static readonly Dictionary<string, (CostChoice Choice, string Field)> Bad = new(StringComparer.Ordinal)
    {
        ["empty id"] = (CostChoice.Sheet(string.Empty), "costs.sheet_id"),
        ["upper case id"] = (CostChoice.Sheet("Fixture-A"), "costs.sheet_id"),
        ["id with a slash"] = (CostChoice.Sheet("a/b"), "costs.sheet_id"),
        ["id of 41 characters"] = (CostChoice.Sheet(new string('a', 41)), "costs.sheet_id"),
        ["capital 0"] = (CostChoice.Sheet("fixture-a", capital: 0m), "costs.capital"),
        ["capital negative"] = (CostChoice.Sheet("fixture-a", capital: -1m), "costs.capital"),
        ["capital above the limit"] = (CostChoice.Sheet("fixture-a", capital: 10_000_000_001m), "costs.capital"),
        ["unknown profile"] = (CostChoice.Sheet("fixture-a", profile: "aggressive"), "costs.profile"),
        ["profile in upper case"] = (CostChoice.Sheet("fixture-a", profile: "Standard"), "costs.profile"),
        ["max cost R 0"] = (CostChoice.Sheet("fixture-a", maxCostR: 0m), "costs.max_cost_r"),
        ["max cost R 101"] = (CostChoice.Sheet("fixture-a", maxCostR: 101m), "costs.max_cost_r"),
        ["max cost R negative"] = (CostChoice.Sheet("fixture-a", maxCostR: -0.1m), "costs.max_cost_r"),
    };

    public static TheoryData<string> BadNames => [.. Bad.Keys];

    [Theory]
    [MemberData(nameof(BadNames))]
    public async Task A_bad_sheet_choice_is_invalid_naming_the_field_and_nothing_is_read(string name)
    {
        var (choice, field) = Bad[name];
        var fixture = WithSheet(out var store);

        var result = await fixture.RunAsync(RunFixture.List([Id], costs: choice), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.InvalidCode, name);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe([field], name);
        store.Finds.ShouldBeEmpty();
        fixture.Engine.Prepared.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task Every_wrong_field_of_the_choice_is_listed_at_once_under_one_code()
    {
        var fixture = WithSheet(out _);

        var result = await fixture.RunAsync(RunFixture.List([Id], costs: CostChoice.Sheet("A B", 0m, "x", 0m)), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().Select(f => f.Field).ShouldBe(["costs.sheet_id", "costs.capital", "costs.profile", "costs.max_cost_r"]);
    }

    [Fact]
    public async Task An_unknown_sheet_is_refused_after_prepare_and_before_any_other_port_is_read()
    {
        var fixture = new RunFixture();
        fixture.CostSheets.OnFind = () =>
        {
            fixture.Engine.Prepared.Count.ShouldBe(1, "the strategy is prepared before the sheet is read");
            fixture.ShouldHaveReadNothing();
        };

        var result = await fixture.RunAsync(RunFixture.Basket(Guid.NewGuid(), costs: CostChoice.Sheet("nope")), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.CostSheetNotFoundCode);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        fixture.CostSheets.Finds.ShouldBe(["nope"]);
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task A_test_sheet_id_is_not_found()
    {
        var fixture = new RunFixture();
        fixture.CostSheets.With(TestCostSheet.Sheet);

        var result = await fixture.RunAsync(RunFixture.List([Id], costs: CostChoice.Sheet(TestCostSheet.Id)), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.CostSheetNotFoundCode, "even a store that held it would not be asked: a test sheet never reaches a run");
        fixture.CostSheets.Finds.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task A_sheet_that_begins_after_the_window_is_refused_before_any_bar_is_read()
    {
        var fixture = WithSheet(out _);
        var day = FixtureSheet.Sheet.Schedules[0].EffectiveFrom.AddDays(-1);

        var result = await fixture.RunAsync(RunFixture.List([Id], day, day.AddDays(5), CostChoice.Sheet(FixtureSheet.Id)), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.CostSheetNotEffectiveCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("costs.sheet_id");
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task A_window_that_starts_on_the_first_effective_date_is_accepted()
    {
        var fixture = WithSheet(out _);
        var id = fixture.Add("NSE:ABB");
        var first = FixtureSheet.Sheet.Schedules[0].EffectiveFrom;

        var result = await fixture.RunAsync(RunFixture.List([id], first, first.AddDays(3), CostChoice.Sheet(FixtureSheet.Id)), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_stored_text_that_no_longer_parses_refuses_the_run_with_the_sheets_own_code()
    {
        var fixture = WithSheet(out var store);
        store.Failure = CostErrors.SheetInvalid("schedules", CostErrors.Required, "is required.");

        var result = await fixture.RunAsync(RunFixture.List([Id], costs: CostChoice.Sheet(FixtureSheet.Id)), Ct);

        result.Error.Code.ShouldBe(CostErrors.SheetInvalidCode);
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task The_store_is_read_once_per_run_and_the_same_context_reaches_every_replay_and_the_header()
    {
        var fixture = WithSheet(out var store);
        var one = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var two = fixture.Add("NSE:ACC", RunFixture.ThreeDays());
        var three = fixture.Add("NSE:EMPTY");

        var result = await fixture.RunAsync(RunFixture.List([one, two, three], costs: CostChoice.Sheet(FixtureSheet.Id, 500_000m, "conservative", 0.2m)), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Finds.ShouldBe([FixtureSheet.Id]);
        var context = fixture.Engine.Replays.Select(r => r.Costs).Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        fixture.Engine.Replays.Count.ShouldBe(3);
        fixture.Engine.Replays.ShouldAllBe(r => ReferenceEquals(r.Costs, context));
        (context.Sheet, context.Capital, context.Profile, context.MaxCostR).ShouldBe((FixtureSheet.Sheet, 500_000m, SizingProfile.Conservative, 0.2m));
        result.Value.Costs.ShouldBe(CostRecord.Of(context));
        result.Value.Costs!.SheetJson.ShouldBe(FixtureSheet.Sheet.ToCanonicalJson(), "no checked note in this sheet, so the text is the full one");
        result.Value.Costs.RatesSha256.ShouldBe(FixtureSheet.Sheet.RatesSha256);
    }

    [Fact]
    public async Task A_gross_choice_reads_no_sheet_and_gives_no_context_and_no_record()
    {
        var fixture = WithSheet(out var store);
        var id = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([id]), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Finds.ShouldBeEmpty();
        fixture.Engine.Replays.ShouldAllBe(r => r.Costs == null);
        result.Value.Costs.ShouldBeNull();
    }

    [Fact]
    public async Task The_start_line_carries_the_sheet_id_or_gross_and_no_number()
    {
        var costed = WithSheet(out _);
        var id = costed.Add("NSE:ABB", RunFixture.ThreeDays());
        var gross = new RunFixture();
        var grossId = gross.Add("NSE:ABB", RunFixture.ThreeDays());

        await costed.RunAsync(RunFixture.List([id], costs: CostChoice.Sheet(FixtureSheet.Id, 750_000m)), Ct);
        await gross.RunAsync(RunFixture.List([grossId]), Ct);

        costed.Log.Events[0].Argument("Costs").ShouldBe(FixtureSheet.Id);
        gross.Log.Events[0].Argument("Costs").ShouldBe("gross");
        costed.Log.Events[0].Message.ShouldNotContain("750000");
        costed.Log.Events[0].Message.ShouldNotContain("0.03");
    }

    [Fact]
    public async Task Every_earlier_refusal_leaves_the_sheet_store_unread()
    {
        var fixture = WithSheet(out var store);
        var bad = RunFixture.List([Id], costs: CostChoice.Sheet(FixtureSheet.Id));

        await fixture.RunAsync(bad with { StrategyId = " " }, Ct);
        fixture.Engine.PrepareResult = Result.Failure<XDrishti.Application.Abstractions.Research.IPreparedStrategy>(SimulationErrors.StrategyInvalid([new FieldError("strategy_id", "not built in", Code: SimulationErrors.UnknownStrategyCode)]));
        await fixture.RunAsync(bad, Ct);
        await fixture.RunAsync(bad with { Fills = new FillRulesInput(6, 1, 0.05, 2) }, Ct);

        store.Finds.ShouldBeEmpty();
        fixture.ShouldHaveReadNothing();
    }

    [Fact]
    public async Task The_other_ports_are_read_after_the_sheet()
    {
        var fixture = WithSheet(out _);
        var id = fixture.Add("NSE:ABB", RunFixture.ThreeDays());
        var order = new List<string>();
        fixture.CostSheets.OnFind = () => order.Add("sheet");
        fixture.Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("calendar");
            return new XDrishti.Domain.Market.Calendar.TradingCalendar();
        });

        await fixture.RunAsync(RunFixture.List([id], costs: CostChoice.Sheet(FixtureSheet.Id)), Ct);

        order.ShouldBe(["sheet", "calendar"]);
    }
}
