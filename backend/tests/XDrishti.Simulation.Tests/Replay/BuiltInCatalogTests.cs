using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>P3-04b2 D3 (criterion 5): the registry is a table; the ids, the catalog and the factory agree, and the next strategy is one row.</summary>
public sealed class BuiltInCatalogTests
{
    [Fact]
    public void The_ids_are_the_ids_of_the_catalog_in_the_same_order()
    {
        BuiltInStrategies.Ids.ShouldBe([.. BuiltInStrategies.Catalog.Select(c => c.Id)]);
        BuiltInStrategies.Ids.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Orb_is_in_the_catalog_with_its_timeframes_and_parameter_defaults()
    {
        var orb = BuiltInStrategies.Catalog.Single(c => c.Id == "ORB");

        orb.Timeframes.ShouldBe([Timeframe.M5, Timeframe.M15]);
        orb.DefaultTimeframe.ShouldBe(Timeframe.M15);
        orb.Parameters.Select(p => (p.Name, p.Default)).ShouldBe([("or_min", 15.0), ("rr", 2.0)]);
        orb.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Vwap_pullback_is_the_second_row_with_15m_only_and_its_two_parameters()
    {
        var entry = BuiltInStrategies.Catalog.Single(c => c.Id == "VWAP_PULLBACK");

        BuiltInStrategies.Ids.ShouldBe(["ORB", "VWAP_PULLBACK"]);
        entry.Timeframes.ShouldBe([Timeframe.M15]);
        entry.DefaultTimeframe.ShouldBe(Timeframe.M15);
        entry.Parameters.Select(p => (p.Name, p.Default)).ShouldBe([("pull_atr", 0.5), ("rr", 2.0)]);
        entry.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Only_pull_atr_declares_a_range_and_it_is_zero_exclusive_to_three()
    {
        var declared = BuiltInStrategies.Catalog.SelectMany(c => c.Parameters.Where(p => p.HasRange).Select(p => (c.Id, p.Name, p.Min, p.Max, p.MinExclusive)));

        declared.ShouldBe([("VWAP_PULLBACK", "pull_atr", (double?)0.0, (double?)3.0, true)]);
    }

    [Fact]
    public void Every_default_satisfies_its_own_range_and_a_range_is_not_empty()
    {
        foreach (var parameter in BuiltInStrategies.Catalog.SelectMany(c => c.Parameters))
        {
            parameter.Contains(parameter.Default).ShouldBeTrue(parameter.Name + " default " + parameter.Default);
            if (parameter is { Min: { } min, Max: { } max })
            {
                min.ShouldBeLessThanOrEqualTo(max, parameter.Name);
            }
        }
    }

    [Fact]
    public void Every_entry_prepares_with_its_own_defaults_on_each_timeframe_it_allows_and_records_the_defaults()
    {
        foreach (var entry in BuiltInStrategies.Catalog)
        {
            entry.Timeframes.ShouldContain(entry.DefaultTimeframe);
            foreach (var timeframe in entry.Timeframes)
            {
                var prepared = BuiltInStrategies.Prepare(entry.Id, null, timeframe);

                prepared.Problems.ShouldBeEmpty(entry.Id + " on " + timeframe.ToCode());
                var strategy = prepared.Strategy.ShouldNotBeNull();
                strategy.Record.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe(entry.Parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => (p.Name, p.Default)));
            }
        }
    }

    [Fact]
    public void An_unknown_id_and_an_unknown_parameter_are_refused_as_before_and_name_the_known_ones()
    {
        var unknown = BuiltInStrategies.Build("orb", null);
        var parameter = BuiltInStrategies.Build("ORB", new Dictionary<string, double> { ["bogus"] = 1 });

        unknown.Definition.ShouldBeNull();
        unknown.Problems.Single().Path.ShouldBe("strategy_id");
        unknown.Problems.Single().Problem.Code.ShouldBe(SimulationErrors.UnknownStrategyCode);
        unknown.Problems.Single().Problem.Message.ShouldContain("`ORB`");
        parameter.Definition.ShouldBeNull();
        parameter.Problems.Single().Path.ShouldBe("parameters.bogus");
        parameter.Problems.Single().Problem.Message.ShouldContain("its parameters are `or_min` and `rr`");
    }
}
