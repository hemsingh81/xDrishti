namespace XDrishti.Application.Market.Refresh;

internal static class RefreshLimits
{
    /// <summary>A refresh that has been "running" this long was interrupted (a healthy one needs a few minutes at most).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    public const int DefaultHistory = 20;

    public const int MaxHistory = 100;
}
