using XDrishti.Indicators;
using XDrishti.Strategies.Lint;

namespace XDrishti.Strategies.Tests.Lint;

/// <summary>The closed table of value ranges behind <c>never_true</c> follows the catalog (spec P3-02f, risks).</summary>
public sealed class ValueRangeTests
{
    [Fact]
    public void Every_name_in_the_table_is_a_catalog_feature()
    {
        var catalog = IndicatorCatalog.All.Select(i => i.Name).ToHashSet();

        ValueRange.Table.Keys.Except(catalog).ShouldBeEmpty("a name of the range table that the catalog does not have");
    }

    [Fact]
    public void Every_range_is_ordered_and_the_table_has_the_twenty_rows_of_the_spec()
    {
        ValueRange.Table.Count.ShouldBe(20);
        ValueRange.Table.Values.ShouldAllBe(r => r.Minimum < r.Maximum);
    }
}
