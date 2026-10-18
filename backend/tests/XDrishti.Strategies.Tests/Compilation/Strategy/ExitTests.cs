using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Compilation.Strategy;

/// <summary>
/// Criterion 11: the exit candidates and the overlays. <c>FIXED_RR</c> and <c>PARTIAL_TRAIL</c> have their tables checked (a valid example, a failing row and a near miss per parameter); the other five candidates and the two
/// overlays pass the shape check only, which is provisional until P3-02b2 (ADR 0036). The tables of the tests are literals written from the spec.
/// </summary>
public sealed class ExitTests
{
    private static ExitParameter P(string name, params double[] values) => Defs.P(name, values);

    private static ExitSpec Trail(params ExitParameter[] parameters) => new(ExitPolicy.PartialTrail, parameters);

    private static StrategyDefinition With(params ExitSpec[] exits) => Defs.Basic().WithBuy(b => b with { Exits = exits }) with { Params = [new StrategyParam("aa", 3), new StrategyParam("bb", 40)] };

    private static StrategyDefinition WithOverlays(params OverlaySpec[] overlays) => Defs.Basic().WithBuy(b => b with { Overlays = overlays }) with { Params = [new StrategyParam("aa", 3)] };

    private static ExitParameter[] FullTrail(double book = 50, double t1 = 1, double trail = 2) => [P("book_pct", book), P("t1_rr", t1), P("trail_atr_mult", trail)];

    [Fact]
    public void FIXED_RR_with_a_list_of_candidate_multiples_compiles_to_the_values_and_reads_no_feature()
    {
        var side = Located.Ok(With(Defs.FixedRr(1.5, 2, 3)));

        var plan = side.Exits.ShouldHaveSingleItem();
        plan.Policy.ShouldBe(ExitPolicy.FixedRr);
        plan.Values.Keys.ShouldBe(["rr"]);
        plan.Values["rr"].ShouldBe([1.5, 2, 3]);
        plan.Reads.ShouldBeEmpty();
    }

    [Fact]
    public void PARTIAL_TRAIL_compiles_to_its_values_and_reads_the_atr_on_the_evaluation_timeframe_with_14_by_default()
    {
        var side = Located.Ok(With(Trail(FullTrail())));

        var plan = side.Exits.ShouldHaveSingleItem();
        plan.Values["book_pct"].ShouldBe([50]);
        plan.Values["t1_rr"].ShouldBe([1]);
        plan.Values["trail_atr_mult"].ShouldBe([2]);
        plan.Values["atr_period"].ShouldBe([14], "the default is part of the plan");
        plan.Reads.ShouldBe([Specs.Of("atr", Timeframe.M15, 14)]);
        side.Features.ShouldContain(Specs.Of("atr", Timeframe.M15, 14));
    }

    [Fact]
    public void PARTIAL_TRAIL_with_an_atr_period_reads_that_atr_and_a_named_parameter_gives_its_number()
    {
        var side = Located.Ok(With(Trail(P("book_pct", 50), new ExitParameter("t1_rr", [Quantity.Named("aa")]), P("trail_atr_mult", 2), P("atr_period", 10))));

        var plan = side.Exits.Single();
        plan.Reads.ShouldBe([Specs.Of("atr", Timeframe.M15, 10)]);
        plan.Values["t1_rr"].ShouldBe([3]);
        plan.Values["atr_period"].ShouldBe([10]);
    }

    // ---- a failing row and a near miss per parameter ------------------------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<string, ExitSpec, string, string> Failing { get; } = new()
    {
        { "rr missing", new ExitSpec(ExitPolicy.FixedRr, []), "model.field_missing", "buy.exits[0].rr" },
        { "rr with no value", new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [])]), "model.field_shape", "buy.exits[0].rr" },
        { "rr with 11 values", Defs.FixedRr(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1.5), "model.field_shape", "buy.exits[0].rr" },
        { "rr with a null list", new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", null!)]), "model.field_shape", "buy.exits[0].rr" },
        { "rr given twice", new ExitSpec(ExitPolicy.FixedRr, [P("rr", 2), P("rr", 3)]), "model.field_shape", "buy.exits[0].rr" },
        { "rr unknown name", new ExitSpec(ExitPolicy.FixedRr, [P("rr", 2), P("rrr", 3)]), "model.field_not_used", "buy.exits[0].rrr" },
        { "rr 0", Defs.FixedRr(0), "model.value_out_of_range", "buy.exits[0].rr[0]" },
        { "rr second value out of range", Defs.FixedRr(2, 10.5), "model.value_out_of_range", "buy.exits[0].rr[1]" },
        { "rr negative", Defs.FixedRr(-1), "model.value_out_of_range", "buy.exits[0].rr[0]" },
        { "rr not finite", new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Of(double.NaN)])]), "model.bad_quantity", "buy.exits[0].rr[0]" },
        { "book_pct missing", Trail(P("t1_rr", 1), P("trail_atr_mult", 2)), "model.field_missing", "buy.exits[0].book_pct" },
        { "t1_rr missing", Trail(P("book_pct", 50), P("trail_atr_mult", 2)), "model.field_missing", "buy.exits[0].t1_rr" },
        { "trail_atr_mult missing", Trail(P("book_pct", 50), P("t1_rr", 1)), "model.field_missing", "buy.exits[0].trail_atr_mult" },
        { "book_pct two values", Trail(P("book_pct", 50, 60), P("t1_rr", 1), P("trail_atr_mult", 2)), "model.field_shape", "buy.exits[0].book_pct" },
        { "t1_rr no value", Trail(P("book_pct", 50), new ExitParameter("t1_rr", []), P("trail_atr_mult", 2)), "model.field_shape", "buy.exits[0].t1_rr" },
        { "trail_atr_mult given twice", Trail(P("book_pct", 50), P("t1_rr", 1), P("trail_atr_mult", 2), P("trail_atr_mult", 3)), "model.field_shape", "buy.exits[0].trail_atr_mult" },
        { "atr_period two values", Trail([.. FullTrail(), P("atr_period", 10, 20)]), "model.field_shape", "buy.exits[0].atr_period" },
        { "an unknown parameter", Trail([.. FullTrail(), P("trail", 2)]), "model.field_not_used", "buy.exits[0].trail" },
        { "book_pct 0", Trail(FullTrail(book: 0)), "model.value_out_of_range", "buy.exits[0].book_pct" },
        { "book_pct 100", Trail(FullTrail(book: 100)), "model.value_out_of_range", "buy.exits[0].book_pct" },
        { "book_pct 101", Trail(FullTrail(book: 101)), "model.value_out_of_range", "buy.exits[0].book_pct" },
        { "t1_rr 0", Trail(FullTrail(t1: 0)), "model.value_out_of_range", "buy.exits[0].t1_rr" },
        { "t1_rr 10.1", Trail(FullTrail(t1: 10.1)), "model.value_out_of_range", "buy.exits[0].t1_rr" },
        { "trail_atr_mult 0", Trail(FullTrail(trail: 0)), "model.value_out_of_range", "buy.exits[0].trail_atr_mult" },
        { "trail_atr_mult 21", Trail(FullTrail(trail: 21)), "model.value_out_of_range", "buy.exits[0].trail_atr_mult" },
        { "atr_period 14.5", Trail([.. FullTrail(), P("atr_period", 14.5)]), "model.feature_invalid", "buy.exits[0].atr_period" },
        { "atr_period 0", Trail([.. FullTrail(), P("atr_period", 0)]), "model.feature_invalid", "buy.exits[0].atr_period" },
        { "unknown param name", new ExitSpec(ExitPolicy.FixedRr, [new ExitParameter("rr", [Quantity.Named("zzz")])]), "model.unknown_param", "buy.exits[0].rr[0]" },
        { "a policy twice", Defs.FixedRr(2), "model.duplicate_exit", "buy.exits[1]" },
    };

    [Theory]
    [MemberData(nameof(Failing))]
    public void An_exit_with_a_problem_gives_its_code_at_its_field_and_nothing_else(string name, ExitSpec exit, string code, string path)
    {
        var exits = code == "model.duplicate_exit" ? new[] { exit, exit } : [exit];

        var result = Located.Compile(With(exits));

        result.ErrorList().ShouldBe([code + "@" + path], name + "\n" + Located.Show(result));
    }

    public static TheoryData<string, ExitSpec> NearMisses { get; } = new()
    {
        { "rr of one value", Defs.FixedRr(2) },
        { "rr of ten values", Defs.FixedRr(1, 2, 3, 4, 5, 6, 7, 8, 9, 10) },
        { "rr at the top of its range", Defs.FixedRr(10) },
        { "rr just above zero", Defs.FixedRr(0.01) },
        { "book_pct just inside", Trail(FullTrail(book: 99.9)) },
        { "book_pct just above zero", Trail(FullTrail(book: 0.1)) },
        { "t1_rr at the top", Trail(FullTrail(t1: 10)) },
        { "trail_atr_mult at the top", Trail(FullTrail(trail: 20)) },
        { "atr_period whole", Trail([.. FullTrail(), P("atr_period", 21)]) },
    };

    [Theory]
    [MemberData(nameof(NearMisses))]
    public void A_near_miss_compiles(string name, ExitSpec exit)
    {
        var result = Located.Compile(With(exit));

        result.IsSuccess.ShouldBeTrue(name + "\n" + Located.Show(result));
    }

    [Fact]
    public void Two_different_candidates_compile_in_the_order_of_the_model_and_a_candidate_twice_is_duplicate_exit_at_the_second()
    {
        var side = Located.Ok(With(Trail(FullTrail()), Defs.FixedRr(2)));
        var twice = Located.Compile(With(Defs.FixedRr(2), Trail(FullTrail()), Defs.FixedRr(3)));

        side.Exits.Select(e => e.Policy).ShouldBe([ExitPolicy.PartialTrail, ExitPolicy.FixedRr]);
        twice.ErrorList().ShouldBe(["model.duplicate_exit@buy.exits[2]"]);
    }

    [Fact]
    public void An_empty_or_null_list_of_exits_gives_exits_empty_and_a_null_exit_is_a_missing_field_and_never_an_exception()
    {
        Located.Compile(With()).ErrorList().ShouldBe(["model.exits_empty@buy.exits"]);
        Located.Compile(Defs.Basic().WithBuy(b => b with { Exits = null })).ErrorList().ShouldBe(["model.exits_empty@buy.exits"]);
        Located.Compile(With([null!])).ErrorList().ShouldBe(["model.field_missing@buy.exits[0]"]);
    }

    // ---- the policies and overlays with the shape check only ----------------------------------------------------------------------------------------------------------------------------

    public static TheoryData<ExitPolicy> ShapeOnlyPolicies { get; } = [ExitPolicy.PartialBe, ExitPolicy.AtrTrail, ExitPolicy.StructureTrail, ExitPolicy.EmaTrail, ExitPolicy.Pyramid];

    [Theory]
    [MemberData(nameof(ShapeOnlyPolicies))]
    public void The_other_five_candidates_compile_when_their_parameters_pass_the_shape_check_and_read_no_feature(ExitPolicy policy)
    {
        var exit = new ExitSpec(policy, [P("any_name", 1, 2, 3), new ExitParameter("other", [Quantity.Named("aa")]), P("big", 5_000)]);

        var side = Located.Ok(With(exit));

        var plan = side.Exits.ShouldHaveSingleItem();
        plan.Policy.ShouldBe(policy);
        plan.Values["any_name"].ShouldBe([1, 2, 3]);
        plan.Values["other"].ShouldBe([3]);
        plan.Values["big"].ShouldBe([5_000], "no name, range or relation is checked in this slice (provisional until P3-02b2)");
        plan.Reads.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ShapeOnlyPolicies))]
    public void A_policy_without_a_table_may_have_no_parameters_at_all(ExitPolicy policy)
    {
        Located.Compile(With(new ExitSpec(policy, []))).IsSuccess.ShouldBeTrue();
        Located.Compile(With(new ExitSpec(policy, null!))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_shape_check_refuses_a_bad_name_a_name_twice_no_value_eleven_values_and_a_value_that_is_neither_a_finite_number_nor_a_declared_parameter()
    {
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [P("Add", 1)]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0]"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [P(new string('a', 33), 1)]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0]"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [P("add", 1), P("add", 2)]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [new ExitParameter("add", [])]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [P("add", 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [new ExitParameter("add", [Quantity.Named("zzz")])]))).ErrorList().ShouldBe(["model.unknown_param@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [new ExitParameter("add", [new Quantity(null, null)])]))).ErrorList().ShouldBe(["model.bad_quantity@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [P("add", double.PositiveInfinity)]))).ErrorList().ShouldBe(["model.bad_quantity@buy.exits[0].add"]);
        Located.Compile(With(new ExitSpec(ExitPolicy.Pyramid, [null!]))).ErrorList().ShouldBe(["model.field_shape@buy.exits[0]"]);
    }

    [Fact]
    public void A_candidate_policy_listed_twice_is_duplicate_exit_whether_or_not_it_has_a_table()
    {
        var pyramid = new ExitSpec(ExitPolicy.Pyramid, [P("add", 1)]);

        Located.Compile(With(pyramid, pyramid)).ErrorList().ShouldBe(["model.duplicate_exit@buy.exits[1]"]);
    }

    [Fact]
    public void Both_overlays_compile_by_shape_and_become_overlay_plans()
    {
        var side = Located.Ok(WithOverlays(
            new OverlaySpec(OverlayKind.TimeStop, [P("min_progress_rr", 0.5), P("intraday_bars", 12)]),
            new OverlaySpec(OverlayKind.EventExit, [new ExitParameter("days_before_results", [Quantity.Named("aa")])])));

        side.Overlays.Select(o => o.Kind).ShouldBe([OverlayKind.TimeStop, OverlayKind.EventExit]);
        side.Overlays[0].Values["intraday_bars"].ShouldBe([12]);
        side.Overlays[1].Values["days_before_results"].ShouldBe([3]);
    }

    [Fact]
    public void An_overlay_listed_twice_is_duplicate_exit_at_the_second_and_an_overlay_shape_fault_is_reported_at_the_overlay()
    {
        var timeStop = new OverlaySpec(OverlayKind.TimeStop, [P("intraday_bars", 12)]);

        Located.Compile(WithOverlays(timeStop, timeStop)).ErrorList().ShouldBe(["model.duplicate_exit@buy.overlays[1]"]);
        Located.Compile(WithOverlays(new OverlaySpec(OverlayKind.TimeStop, [P("Bars", 12)]))).ErrorList().ShouldBe(["model.field_shape@buy.overlays[0]"]);
        Located.Compile(WithOverlays([null!])).ErrorList().ShouldBe(["model.field_missing@buy.overlays[0]"]);
    }

    [Fact]
    public void TIME_STOP_EVENT_EXIT_SESSION_EXIT_and_MAX_HOLD_are_not_members_of_the_exit_policy_enum()
    {
        Enum.GetNames<ExitPolicy>().ShouldBe(["FixedRr", "PartialBe", "PartialTrail", "AtrTrail", "StructureTrail", "EmaTrail", "Pyramid"]);
        Enum.GetNames<OverlayKind>().ShouldBe(["TimeStop", "EventExit"]);
    }
}
