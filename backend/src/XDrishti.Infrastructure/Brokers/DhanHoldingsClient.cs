using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// Reads the account's holdings from DhanHQ v2 (<c>GET /v2/holdings</c>, header <c>access-token</c>; ADR 0012). Only that one path is allowed
/// by <see cref="BrokerEndpointGuard"/>; no redirects, 30-second timeout, answers up to 5 MB. The token is only put into the request header and
/// neither it, the URL nor Dhan's answer is ever logged or put into an error.
/// </summary>
internal sealed class DhanHoldingsClient(IHttpClientFactory httpClients, ILogger<DhanHoldingsClient> logger) : IBrokerHoldings
{
    /// <summary>
    /// Dhan's documented data error DH-907 ("incorrect parameters or no data present"). The holdings request has no parameters, so on it the code can only
    /// mean "no data": an empty list, not a failure.
    /// </summary>
    internal const string NoDataCode = "DH-907";

    /// <summary>DH-1111 is not in Dhan's documentation; it was reported for an account without holdings, so it is tolerated as an alias of <see cref="NoDataCode"/>.</summary>
    internal const string NoHoldingsAliasCode = "DH-1111";

    /// <summary>More rows than any account has; a bigger answer is treated as unusable.</summary>
    internal const int MaxRows = 5000;

    private const string Operation = "holdings";

    public async ValueTask<BrokerResponse<IReadOnlyList<BrokerHolding>>> FetchAsync(SecretValue accessToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, BrokerEndpoints.DhanHoldings);
        message.Headers.TryAddWithoutValidation("access-token", accessToken.Reveal());
        using var client = httpClients.CreateClient(BrokerHttpClientNames.DhanHoldings);
        var answer = await BrokerHttp.SendAsync(client, message, Operation, logger, cancellationToken, tokenCall: true);
        if (!answer.IsSuccess)
        {
            var failure = answer.Failure!;
            return failure is { Kind: BrokerFailureKind.Rejected, BrokerCode: NoDataCode or NoHoldingsAliasCode }
                ? BrokerResponse.Ok<IReadOnlyList<BrokerHolding>>([])
                : BrokerResponse.Fail<IReadOnlyList<BrokerHolding>>(failure);
        }

        return answer.Value.ValueKind == JsonValueKind.Array && answer.Value.GetArrayLength() <= MaxRows
            ? BrokerResponse.Ok<IReadOnlyList<BrokerHolding>>(Parse(answer.Value))
            : BrokerHttp.Unexpected<IReadOnlyList<BrokerHolding>>(answer.Value, Operation, logger);
    }

    /// <summary>
    /// Dhan's rows: <c>exchange, tradingSymbol, securityId, isin, totalQty, avgCostPrice, lastTradedPrice</c> (the other fields are not needed).
    /// A row without symbol or security id is dropped; a missing average cost or price reads as 0.
    /// </summary>
    private static List<BrokerHolding> Parse(JsonElement rows)
    {
        var holdings = new List<BrokerHolding>(rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var securityId = BrokerHttp.Text(row, "securityId");
            var symbol = BrokerHttp.Text(row, "tradingSymbol");
            if (securityId is null || symbol is null)
            {
                continue;
            }

            holdings.Add(new BrokerHolding(
                BrokerHttp.Text(row, "exchange") ?? "ALL",
                securityId,
                BrokerHttp.Text(row, "isin"),
                symbol,
                (long)Math.Truncate(Number(row, "totalQty")),
                Number(row, "avgCostPrice"),
                Number(row, "lastTradedPrice")));
        }

        return holdings;
    }

    /// <summary>Numbers beyond this are not quantities or prices (and would not fit the columns): read as missing.</summary>
    private const decimal Sane = 1_000_000_000_000m;

    private static decimal Number(JsonElement row, string name)
    {
        var number = Raw(row, name);
        return Math.Abs(number) > Sane ? 0m : number;
    }

    private static decimal Raw(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value))
        {
            return 0m;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0m,
        };
    }
}
