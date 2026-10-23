using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The grid source, the trigger rules and the run-definition reader of the hypothesis loop (spec P6-10).</summary>
public sealed class HypothesisSupportTests
{
    private const string Record = "{\"strategy\":{\"id\":\"ORB\",\"parameters\":{\"rr\":2,\"or_min\":15},\"definition_json\":\"{\\\"id\\\":\\\"ORB\\\",\\\"params\\\":[{\\\"name\\\":\\\"rr\\\",\\\"value\\\":2}],\\\"buy\\\":{\\\"trigger\\\":{},\\\"exits\\\":[{\\\"policy\\\":\\\"fixed_rr\\\"}]},\\\"sell\\\":{\\\"exits\\\":[]}}\"}}";

    [Fact]
    public void The_grid_is_ten_and_twenty_percent_each_side_and_deterministic()
    {
        new HypothesisGrid().Around(2).ShouldBe(["1.6", "1.8", "2.2", "2.4"]);
        new HypothesisGrid().Around(2).ShouldBe(new HypothesisGrid().Around(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void No_grid_exists_around_zero_or_a_non_number(double current)
    {
        new HypothesisGrid().Around(current).ShouldBeEmpty();
    }

    [Fact]
    public void A_negative_value_keeps_its_sign_and_the_values_are_ascending()
    {
        new HypothesisGrid(10, 1).Around(-2).ShouldBe(["-2.2", "-1.8"]);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(51, 2)]
    [InlineData(10, 0)]
    [InlineData(10, 5)]
    [InlineData(30, 4)]
    public void Grid_options_are_bounded_so_no_value_changes_sign(double step, int steps)
    {
        new HypothesisGrid(step, steps).Problems().ShouldNotBeEmpty();
        new HypothesisGrid(step, steps).Around(2).ShouldBeEmpty();
    }

    [Fact]
    public void A_value_is_on_the_grid_when_it_is_one_as_a_number()
    {
        var grid = new HypothesisGrid();

        grid.Contains(2, "2.40").ShouldBeTrue();
        grid.Contains(2, "2.5").ShouldBeFalse();
        grid.Contains(2, "2").ShouldBeFalse();
        grid.Contains(2, "x").ShouldBeFalse();
    }

    [Fact]
    public void New_trades_trigger_a_cell_at_the_threshold_only_for_trades_after_the_date()
    {
        var since = new DateOnly(2026, 5, 1);
        var trades = Enumerable.Range(0, 30).Select(_ => ("B", new DateOnly(2026, 5, 2)))
            .Concat(Enumerable.Range(0, 29).Select(_ => ("A", new DateOnly(2026, 5, 2))))
            .Concat(Enumerable.Range(0, 50).Select(_ => ("C", since)));

        var triggers = HypothesisTriggers.NewTrades(trades, since, 30);

        triggers.Select(t => t.CellKey).ShouldBe(["B"]);
        triggers[0].Kind.ShouldBe("new_trades");
    }

    [Fact]
    public void Drift_triggers_come_from_pause_proposals_once_per_cell()
    {
        var triggers = HypothesisTriggers.Drift(
        [
            new DriftProposal("B", 2, true, "below"),
            new DriftProposal("B", 3, true, "again"),
            new DriftProposal("A", 1, false, "recovered"),
            new DriftProposal("A", 4, true, "cusum"),
        ]);

        triggers.Select(t => (t.CellKey, t.Detail)).ShouldBe([("A", "cusum"), ("B", "below")]);
    }

    [Fact]
    public void A_run_record_gives_its_parameters_and_splits_the_definition_from_params_and_exits()
    {
        var def = RunDefinition.TryRead(Record, "fills", out var problem);

        problem.ShouldBeNull();
        def!.StrategyId.ShouldBe("ORB");
        def.Parameters["rr"].ShouldBe(2);
        def.Rest.ShouldContain("\"name\":\"rr\"");
        def.Rest.ShouldNotContain("\"value\"");
        def.Rest.ShouldNotContain("fixed_rr");
        def.Rest.ShouldContain("trigger");
        def.Exits.ShouldContain("fixed_rr");
        def.Fills.ShouldBe("fills");
    }

    [Fact]
    public void Property_order_does_not_matter_and_a_range_in_params_does()
    {
        string Rec(string definition) => "{\"strategy\":{\"id\":\"A\",\"parameters\":{},\"definition_json\":\"" + definition.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}}";

        var a = RunDefinition.TryRead(Rec("{\"id\":\"A\",\"params\":[{\"name\":\"rr\",\"value\":2,\"max\":5}]}"), "f", out _)!;
        var reordered = RunDefinition.TryRead(Rec("{\"params\":[{\"max\":5,\"value\":3,\"name\":\"rr\"}],\"id\":\"A\"}"), "f", out _)!;
        var wider = RunDefinition.TryRead(Rec("{\"id\":\"A\",\"params\":[{\"name\":\"rr\",\"value\":2,\"max\":9}]}"), "f", out _)!;

        reordered.Rest.ShouldBe(a.Rest);
        wider.Rest.ShouldNotBe(a.Rest);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"strategy\":{\"id\":\"A\",\"parameters\":{\"x\":\"s\"},\"definition_json\":\"{}\"}}")]
    [InlineData("{\"strategy\":{\"id\":\"A\",\"parameters\":{},\"definition_json\":\"[1]\"}}")]
    public void An_unreadable_record_is_a_problem_not_an_exception(string record)
    {
        RunDefinition.TryRead(record, "f", out var problem).ShouldBeNull();
        problem.ShouldNotBeNull();
    }
}
