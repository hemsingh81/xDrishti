using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Recon;

namespace XDrishti.Execution.Application.Recon;

/// <summary>
/// The tolerant reader of one order-update message (spec P12-06, UNVERIFIED: the field names are from one documentation page whose own sample is malformed JSON). It does not parse JSON: it reads <c>"name": value</c> pairs wherever they stand, matching names without
/// regard to case, the first non-empty value of a name winning, and ignoring blanks and unknown names. A message that is not an order alert is ignored; one that is garbled, lacks a field or carries an unknown code is <c>Unreadable</c> (dropped, counted, a poll is asked for) and
/// never guessed. Only the mapped fields leave this class: no remarks, no client id, no token.
/// </summary>
public static partial class OrderUpdateParser
{
    public const int MaxChars = 16 * 1024;

    [GeneratedRegex("\"(?<k>[^\"\\\\]{1,64})\"\\s*:\\s*(?:\"(?<s>(?:[^\"\\\\]|\\\\.)*)\"|(?<n>-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)|(?<b>true|false|null)|)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Pair();

    public static UpdateParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxChars)
        {
            return UpdateParseResult.Unreadable(text is null || text.Trim().Length == 0 ? "empty" : "too_long");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Match m in Pair().Matches(text))
            {
                var value = m.Groups["s"].Success ? m.Groups["s"].Value : m.Groups["n"].Success ? m.Groups["n"].Value : m.Groups["b"].Success ? m.Groups["b"].Value : string.Empty;
                if (value.Length > 0 && value != "null")
                {
                    fields.TryAdd(m.Groups["k"].Value, value);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return UpdateParseResult.Unreadable("timeout");
        }

        if (fields.Count == 0)
        {
            return UpdateParseResult.Unreadable("no_fields");
        }

        if (!fields.TryGetValue("Type", out var type))
        {
            return UpdateParseResult.Unreadable("no_type");
        }

        if (!string.Equals(type, "order_alert", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateParseResult(UpdateParseKind.NotAnOrderAlert, null, null);
        }

        if (!fields.TryGetValue("OrderNo", out var orderNo) || orderNo.Length > 40)
        {
            return UpdateParseResult.Unreadable("order_no");
        }

        if (UpdateMapping.Status(fields.GetValueOrDefault("Status")) is not { } status)
        {
            return UpdateParseResult.Unreadable("status_unknown");
        }

        if (UpdateMapping.Side(fields.GetValueOrDefault("TxnType")) is not { } side)
        {
            return UpdateParseResult.Unreadable("txn_type_unknown");
        }

        if (UpdateMapping.Product(fields.GetValueOrDefault("Product")) is not { } product)
        {
            return UpdateParseResult.Unreadable("product_unknown");
        }

        if (UpdateMapping.OrderType(fields.GetValueOrDefault("OrderType")) is not { } orderType)
        {
            return UpdateParseResult.Unreadable("order_type_unknown");
        }

        if (!Int(fields, "LegNo", out var legNo) || legNo is < 1 or > 3)
        {
            return UpdateParseResult.Unreadable("leg_no_unknown");
        }

        if (!Int(fields, "Quantity", out var quantity) || quantity <= 0)
        {
            return UpdateParseResult.Unreadable("quantity");
        }

        var traded = 0;
        if (fields.ContainsKey("TradedQty") && (!Int(fields, "TradedQty", out traded) || traded < 0 || traded > quantity))
        {
            return UpdateParseResult.Unreadable("traded_qty");
        }

        if (!Dec(fields, "Price", out var price) || !Dec(fields, "TriggerPrice", out var trigger) || !Dec(fields, "AvgTradedPrice", out var avg) || price < 0 || trigger < 0 || avg < 0)
        {
            return UpdateParseResult.Unreadable("price");
        }

        var correlation = fields.GetValueOrDefault("CorrelationId");
        return UpdateParseResult.Of(new OrderUpdate(
            orderNo, correlation is { Length: <= 40 } ? correlation : null, status, legNo, side, product, orderType, quantity, traded, price, trigger, avg,
            Short(fields.GetValueOrDefault("ExchOrderNo")), Short(fields.GetValueOrDefault("AlgoId")), Short(fields.GetValueOrDefault("Source")), Short(fields.GetValueOrDefault("LastUpdatedTime"))));
    }

    /// <summary>The login message of the stream, exactly <c>{"LoginReq":{"MsgCode":42,"ClientId":"…","Token":"…"},"UserType":"SELF"}</c>. The token travels in this message only, never in the URL and never in a log.</summary>
    public static string LoginMessage(string clientId, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartObject("LoginReq");
            w.WriteNumber("MsgCode", 42);
            w.WriteString("ClientId", clientId);
            w.WriteString("Token", token);
            w.WriteEndObject();
            w.WriteString("UserType", "SELF");
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? Short(string? value) => value is { Length: > 0 and <= 40 } ? value : null;

    private static bool Int(Dictionary<string, string> fields, string name, out int value)
    {
        value = 0;
        return fields.TryGetValue(name, out var text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // An absent price field is zero (a market order has no price); a present one must be a number.
    private static bool Dec(Dictionary<string, string> fields, string name, out decimal value)
    {
        value = 0m;
        return !fields.TryGetValue(name, out var text) || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>The code tables of the stream. An unknown code maps to null: the update is unreadable and a poll decides.</summary>
public static class UpdateMapping
{
    public static UpdateStatus? Status(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "TRANSIT" => UpdateStatus.Transit,
        "PENDING" => UpdateStatus.Pending,
        "REJECTED" => UpdateStatus.Rejected,
        "CANCELLED" => UpdateStatus.Cancelled,
        "TRADED" => UpdateStatus.Traded,
        "EXPIRED" => UpdateStatus.Expired,
        _ => null,
    };

    public static ExecSide? Side(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "B" => ExecSide.Buy,
        "S" => ExecSide.Sell,
        _ => null,
    };

    /// <summary>C CNC, I intraday, M margin, F MTF, V CO, B BO.</summary>
    public static string? Product(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "C" => "CNC",
        "I" => "INTRADAY",
        "M" => "MARGIN",
        "F" => "MTF",
        "V" => "CO",
        "B" => "BO",
        _ => null,
    };

    public static string? OrderType(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "LMT" => "LIMIT",
        "MKT" => "MARKET",
        "SL" => "STOP_LOSS",
        "SLM" => "STOP_LOSS_MARKET",
        _ => null,
    };
}
