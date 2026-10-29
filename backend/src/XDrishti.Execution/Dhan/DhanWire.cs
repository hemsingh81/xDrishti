using System.Text.Json;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// Request bodies. Only a LIMIT, DAY, INTRADAY order on NSE equity can be expressed: no market order, no after-market flag, no disclosed quantity, no other product. A stop or target leg needs a trigger price and the super-order structure, which
/// P12-04 decides, so they are refused here. JSON, not form fields (Dhan's intro says form, every example is JSON; UNVERIFIED until the sandbox smoke).
/// </summary>
public static class DhanWire
{
    public const decimal Tick = 0.05m;

    public const string InstructionInvalid = "execution.instruction_invalid";

    /// <summary>Null when valid, else the reason code.</summary>
    public static string? Validate(BrokerLeg leg)
    {
        if (leg.ClientOrderId is null || !ClientOrderId.TryParse(leg.ClientOrderId, out _))
        {
            return "client_order_id";
        }

        if (leg.Kind is LegKind.Stop or LegKind.Target)
        {
            return "leg_kind_not_supported";
        }

        if (leg.Qty <= 0 || leg.Price <= 0m || leg.Price % Tick != 0m)
        {
            return leg.Qty <= 0 ? "quantity" : "price_or_tick";
        }

        return leg.SecurityId is not { Length: > 0 and <= 12 } security || !security.All(char.IsAsciiDigit) ? "security_id" : null;
    }

    public static byte[] PlaceBody(DhanCredentials credentials, BrokerLeg leg)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("dhanClientId", credentials.ClientId);
            w.WriteString("correlationId", leg.ClientOrderId);
            w.WriteString("transactionType", leg.Side == ExecSide.Buy ? "BUY" : "SELL");
            w.WriteString("exchangeSegment", "NSE_EQ");
            w.WriteString("productType", "INTRADAY");
            w.WriteString("orderType", "LIMIT");
            w.WriteString("validity", "DAY");
            w.WriteString("securityId", leg.SecurityId);
            w.WriteNumber("quantity", leg.Qty);
            w.WriteNumber("price", leg.Price);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] ModifyBody(DhanCredentials credentials, string brokerOrderId, int qty, decimal price)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("dhanClientId", credentials.ClientId);
            w.WriteString("orderId", brokerOrderId);
            w.WriteString("orderType", "LIMIT");
            w.WriteString("validity", "DAY");
            w.WriteNumber("quantity", qty);
            w.WriteNumber("price", price);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }
}
