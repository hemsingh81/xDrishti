using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Application.OptionChain;

/// <summary>
/// Dhan's answer to an outcome word, pure (P14-02 criterion 6). Codes are the annexure's: <c>DH-901</c> (and HTTP 401, 403) token refused, <c>DH-902</c> not subscribed to the Data APIs, <c>DH-904</c> (and HTTP 429) rate limit, <c>DH-907</c> no data,
/// <c>DH-908</c>/<c>DH-909</c> and 5xx server. 807 and 809 are the token codes of the sockets and 806 their not-subscribed code; both are read from the numeric form too.
/// </summary>
public static class ChainOutcomeMap
{
    public static string From(int? httpStatus, string? dhanCode)
    {
        switch (dhanCode)
        {
            case "DH-901" or "807" or "809":
                return ChainOutcomes.TokenRefused;
            case "DH-902" or "806":
                return ChainOutcomes.NotSubscribed;
            case "DH-904":
                return ChainOutcomes.RateLimited;
            case "DH-907":
                return ChainOutcomes.NoData;
            case "DH-908" or "DH-909":
                return ChainOutcomes.ServerError;
            default:
                break;
        }

        return httpStatus switch
        {
            429 => ChainOutcomes.RateLimited,
            401 or 403 => ChainOutcomes.TokenRefused,
            >= 200 and < 300 => ChainOutcomes.Ok,
            _ => ChainOutcomes.ServerError,
        };
    }
}
