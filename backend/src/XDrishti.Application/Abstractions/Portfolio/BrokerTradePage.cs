namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// One page of Dhan's trade history after mapping: the usable NSE equity <see cref="Trades"/>, how many rows the page had (<see cref="Rows"/>; 0 is the end of the history), how many were unusable (<see cref="Dropped"/>) and how many were
/// skipped per <see cref="BrokerSkipReasons"/> reason. <see cref="NoData"/> says the page is empty because Dhan answered its data error DH-907 ("incorrect parameters or no data present") instead of an empty list: that code means "nothing there" at the end of the
/// paging and anywhere else it is unclear (it is also the answer to a bad request), so the reader decides what it means. Counts only: nothing of the rows' content.
/// </summary>
public sealed record BrokerTradePage(IReadOnlyList<BrokerTrade> Trades, int Rows, int Dropped, IReadOnlyDictionary<string, int> Skipped, bool NoData = false)
{
    /// <summary>Why the <see cref="Dropped"/> rows were dropped (roadmap ISS-03): per reason code and the index of the first few. A source that does not say leaves it empty (the reader then counts them as <c>other</c>).</summary>
    public BrokerReadDrops Drops { get; init; } = BrokerReadDrops.None;

    public static BrokerTradePage Empty { get; } = new([], 0, 0, new Dictionary<string, int>());

    /// <summary>The page of an answer that was Dhan's "no data" code (DH-907).</summary>
    public static BrokerTradePage NoDataAnswer { get; } = Empty with { NoData = true };
}
