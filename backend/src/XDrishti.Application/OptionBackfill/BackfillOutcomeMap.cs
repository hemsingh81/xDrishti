using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Application.OptionBackfill;

/// <summary>
/// Dhan's answer to an outcome word, pure (P14-03 criterion 5). Codes are the annexure's: <c>DH-901</c> (and HTTP 401, 403) token refused, <c>DH-902</c> not subscribed to the Data APIs, <c>DH-904</c> (and HTTP 429) rate limit, <c>DH-905</c> bad input (the run ends), <c>DH-907</c> no data,
/// <c>DH-908</c>/<c>DH-909</c> and 5xx server. 806 is the socket-style not-subscribed code, 807 and 809 the token codes; all three are read too.
/// </summary>
public static class BackfillOutcomeMap
{
    public static string From(int? httpStatus, string? dhanCode)
    {
        switch (dhanCode)
        {
            case "DH-901" or "807" or "809":
                return BackfillOutcomes.TokenRefused;
            case "DH-902" or "806":
                return BackfillOutcomes.NotSubscribed;
            case "DH-905":
                return BackfillOutcomes.BadRequest;
            case "DH-904":
                return BackfillOutcomes.RateLimited;
            case "DH-907":
                return BackfillOutcomes.NoData;
            case "DH-908" or "DH-909":
                return BackfillOutcomes.ServerError;
            default:
                break;
        }

        return httpStatus switch
        {
            429 => BackfillOutcomes.RateLimited,
            401 or 403 => BackfillOutcomes.TokenRefused,
            >= 200 and < 300 => BackfillOutcomes.Ok,
            _ => BackfillOutcomes.ServerError,
        };
    }
}
