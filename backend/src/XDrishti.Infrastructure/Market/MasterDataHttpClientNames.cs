namespace XDrishti.Infrastructure.Market;

/// <summary>Names of the HTTP clients that download the instrument master files and NSE's holiday list (tests replace their primary handlers with fakes).</summary>
public static class MasterDataHttpClientNames
{
    public const string Dhan = "xd-master-dhan";

    public const string Nse = "xd-master-nse";

    /// <summary>NSE's public JSON API (the trading-holiday list), which expects a browser-like client.</summary>
    public const string NseApi = "xd-master-nse-api";
}
