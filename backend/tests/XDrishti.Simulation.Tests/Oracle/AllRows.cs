namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Every encoded hand row.</summary>
internal static class AllRows
{
    private static readonly Lazy<IReadOnlyList<HandRow>> Cache = new(() =>
        [.. RowsP303.All(), .. Rows3b.All(), .. RowsSwing.All()]);

    internal static IReadOnlyList<HandRow> Rows => Cache.Value;

    public static TheoryData<string> Ids()
    {
        var d = new TheoryData<string>();
        foreach (HandRow r in Rows)
        {
            d.Add(r.Id);
        }

        return d;
    }

    internal static HandRow Get(string id) => Rows.Single(r => r.Id == id);
}
