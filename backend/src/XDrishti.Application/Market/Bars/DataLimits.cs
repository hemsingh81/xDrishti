namespace XDrishti.Application.Market.Bars;

internal static class DataLimits
{
    /// <summary>A job that has been "running" this long was interrupted. The first CSV load of 40 million rows takes minutes, a Dhan fetch for a few hundred instruments about as long; nothing healthy needs half a day.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    public const int DefaultHistory = 20;

    public const int MaxHistory = 100;

    public const int DefaultBars = 100;

    public const int MaxBars = 1000;
}
