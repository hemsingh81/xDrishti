using System.Globalization;
using System.Text.Json;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

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
            : new BrokerOrderView(id, Str(row, "correlationId"), status.ToUpperInvariant(), security, Side(row), qty, Int(row, out var filled, "filled_qty", "filledQty") ? filled : 0, price);
    });

    public static BrokerRead<IReadOnlyList<BrokerTradeView>> TradeBook(string text) => Rows(text, row =>
    {
        var id = Str(row, "orderId");
        var security = Str(row, "securityId");
        return id is null || security is null || !Int(row, out var qty, "tradedQuantity", "quantity") || !Dec(row, out var price, "tradedPrice", "price")
            ? null
            : new BrokerTradeView(id, security, Side(row), qty, price);
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
