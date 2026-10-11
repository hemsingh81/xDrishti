namespace XDrishti.Infrastructure.Market;

/// <summary>Names of the HTTP clients that download the instrument master files (tests replace their primary handlers with fakes).</summary>
public static class MasterDataHttpClientNames
{
    public const string Dhan = "xd-master-dhan";

    public const string Nse = "xd-master-nse";
}
