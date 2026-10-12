namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// The result of one data quality check, and of an instrument as a whole (the worst of its checks). The spelling is part of the API and of the stored results:
/// <c>pass</c> · <c>warning</c> · <c>fail</c>; a check that cannot be made yet says <c>notAvailable</c> (the comparison with the official daily candle, ADR 0017 §4).
/// </summary>
public static class CheckStatus
{
    public const string Pass = "pass";
    public const string Warning = "warning";
    public const string Fail = "fail";
    public const string NotAvailable = "notAvailable";

    public static bool IsKnownResult(string? status) => status is Pass or Warning or Fail;

    /// <summary>The more serious of the two (fail over warning over pass); <see cref="NotAvailable"/> never wins.</summary>
    public static string Worst(string a, string b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(string status) => status switch
    {
        Fail => 3,
        Warning => 2,
        Pass => 1,
        _ => 0,
    };
}
