namespace XDrishti.Infrastructure.Brokers;

/// <summary>Name of the HTTP client the Dhan connector uses (tests replace its primary handler with a fake Dhan).</summary>
public static class BrokerHttpClientNames
{
    public const string Dhan = "xd-broker-dhan";

    /// <summary>The Dhan client for the holdings read: longer timeout and larger answers than the consent flow (tests replace its primary handler too).</summary>
    public const string DhanHoldings = "xd-broker-dhan-holdings";
}
