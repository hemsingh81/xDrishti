using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Market;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// Tolerant parsers for the four read answers (P12-02b). Unknown fields are ignored; names are matched without regard to case (the documented quirks such as <c>availabelBalance</c> are matched by name); a row that cannot be read makes the whole view
/// unreadable, never a shorter list. The field names are from Dhan's pages and UNVERIFIED against the sandbox.
/// </summary>
internal static class DhanReads
{
    public static BrokerRead<IReadOnlyList<BrokerOrderView>> OrderBook(string text) => Rows(text, row =>
    {
        var id = Str(row, "orderId");
        var status = Str(row, "orderStatus");
        var security = Str(row, "securityId");
        return id is null || status is null || security is null || !Int(row, out var qty, "quantity") || !Dec(row, out var price, "price")
            ? null
            : new BrokerOrderView(id, Str(row, "correlationId"), status.ToUpperInvariant(), security, Side(row), qty, Int(row, out var filled, "filled_qty", "filledQty") ? filled : 0, price,
                Dec(row, out var trigger, "triggerPrice") && trigger > 0m ? trigger : null, Str(row, "legName"));
    });

    public static BrokerRead<IReadOnlyList<BrokerTradeView>> TradeBook(string text) => Rows(text, row =>
    {
        var id = Str(row, "orderId");
        var security = Str(row, "securityId");
        return id is null || security is null || !Int(row, out var qty, "tradedQuantity", "quantity") || !Dec(row, out var price, "tradedPrice", "price")
            ? null
            : new BrokerTradeView(id, security, Side(row), qty, price, Str(row, "exchangeTradeId"));
    });

    public static BrokerRead<IReadOnlyList<BrokerPositionView>> Positions(string text) => Rows(text, row =>
        Str(row, "securityId") is { } security && Int(row, out var net, "netQty") ? new BrokerPositionView(security, net) : null);

    public static BrokerRead<BrokerFundsView> Funds(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Dec(doc.RootElement, out var available, "availabelBalance", "availableBalance")
                ? BrokerRead.Ok<BrokerFundsView>(new BrokerFundsView(available))
                : BrokerRead.Unreadable<BrokerFundsView>("funds_unreadable");
        }
        catch (JsonException)
        {
            return BrokerRead.Unreadable<BrokerFundsView>("funds_unreadable");
        }
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy"];

    /// <summary>The profile, reduced to <c>tokenValidity</c> (India time, "dd/MM/yyyy HH:mm"). No other field is kept; an unreadable or missing validity is a profile without one, never a guessed one. UNVERIFIED against the sandbox.</summary>
    public static BrokerRead<ProfileFacts> Profile(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return BrokerRead.Unreadable<ProfileFacts>("profile_unreadable");
            }

            DateTimeOffset? valid = Str(doc.RootElement, "tokenValidity") is { } s && DateTime.TryParseExact(s, ["dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                ? new DateTimeOffset(at, IndiaTime.Offset)
                : null;
            return BrokerRead.Ok(new ProfileFacts(valid));
        }
        catch (JsonException)
        {
            return BrokerRead.Unreadable<ProfileFacts>("profile_unreadable");
        }
    }

    /// <summary>The configured static IPs (<c>primaryIP</c>, <c>secondaryIP</c>, <c>modifyDatePrimary</c>, <c>modifyDateSecondary</c>). An empty slot is null; the answer says nothing about the address a request came from. UNVERIFIED against the sandbox.</summary>
    public static BrokerRead<IpFacts> StaticIp(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return BrokerRead.Unreadable<IpFacts>("ip_unreadable");
            }

            var root = doc.RootElement;
            return BrokerRead.Ok(new IpFacts(Str(root, "primaryIP"), Str(root, "secondaryIP"), Date(Str(root, "modifyDatePrimary")), Date(Str(root, "modifyDateSecondary"))));
        }
        catch (JsonException)
        {
            return BrokerRead.Unreadable<IpFacts>("ip_unreadable");
        }
    }

    private static DateOnly? Date(string? s) => s is not null && DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static BrokerRead<IReadOnlyList<T>> Rows<T>(string text, Func<JsonElement, T?> read)
        where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return BrokerRead.Unreadable<IReadOnlyList<T>>("not_a_list");
            }

            List<T> rows = [];
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object || read(element) is not { } row)
                {
                    return BrokerRead.Unreadable<IReadOnlyList<T>>("row_unreadable");
                }

                rows.Add(row);
            }

            return BrokerRead.Ok<IReadOnlyList<T>>(rows);
        }
        catch (JsonException)
        {
            return BrokerRead.Unreadable<IReadOnlyList<T>>("not_json");
        }
    }

    private static string? Str(JsonElement row, string name) => DhanErrorMap.Str(row, name) is { Length: > 0 } s ? s : null;

    private static ExecSide? Side(JsonElement row) => Str(row, "transactionType")?.ToUpperInvariant() switch
    {
        "BUY" => ExecSide.Buy,
        "SELL" => ExecSide.Sell,
        _ => null,
    };

    private static bool Int(JsonElement row, out int value, params string[] names)
    {
        foreach (var name in names)
        {
            if (Str(row, name) is { } s)
            {
                return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
            }
        }

        value = 0;
        return false;
    }

    private static bool Dec(JsonElement row, out decimal value, params string[] names)
    {
        foreach (var name in names)
        {
            if (Str(row, name) is { } s)
            {
                return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            }
        }

        value = 0m;
        return false;
    }
}
