using System.Text.Json;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// Request bodies. Only a LIMIT (or, for a protective stop, a STOP_LOSS limit), DAY, INTRADAY order on NSE equity can be expressed: no market order, no after-market flag, no disclosed quantity, no other product. A resting target leg is refused:
/// the target is an engine rule (P12-04), never a second resting sell. The stop-loss order type, <c>triggerPrice</c> and the super-order body are from Dhan's pages and UNVERIFIED against the sandbox. JSON, not form fields (Dhan's intro says form, every example is JSON; UNVERIFIED until the sandbox smoke).
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

        if (leg.Kind is LegKind.Target)
        {
            return "leg_kind_not_supported";
        }

        if (leg.Kind is LegKind.Exit or LegKind.Flatten && leg.Side != ExecSide.Sell)
        {
            return "exit_side"; // protective legs only reduce a long: sell-only in the wire as well as in the policy
        }

        if (leg.Kind == LegKind.Stop)
        {
            if (leg.Side != ExecSide.Sell || leg.Super is not null)
            {
                return "stop_wrong_side";
            }

            if (leg.Trigger is not { } trigger || trigger <= 0m || trigger % Tick != 0m || leg.Price > trigger)
            {
                return "stop_trigger";
            }
        }
        else if (leg.Trigger is not null)
        {
            return "trigger_not_allowed";
        }

        if (leg.Super is { } super)
        {
            if (leg.Kind != LegKind.Entry || leg.Side != ExecSide.Buy || super.StopLoss <= 0m || super.StopLoss % Tick != 0m || super.Target % Tick != 0m || super.StopLoss >= leg.Price || super.Target <= leg.Price)
            {
                return "super_legs";
            }
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
            w.WriteString("orderType", leg.Kind == LegKind.Stop ? "STOP_LOSS" : "LIMIT");
            w.WriteString("validity", "DAY");
            w.WriteString("securityId", leg.SecurityId);
            w.WriteNumber("quantity", leg.Qty);
            w.WriteNumber("price", leg.Price);
            if (leg.Kind == LegKind.Stop)
            {
                w.WriteNumber("triggerPrice", leg.Trigger!.Value);
            }

            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The super-order entry (UNVERIFIED): the entry limit with the target and the stop-loss prices and no trailing jump.</summary>
    public static byte[] SuperPlaceBody(DhanCredentials credentials, BrokerLeg leg)
    {
        var super = leg.Super!;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("dhanClientId", credentials.ClientId);
            w.WriteString("correlationId", leg.ClientOrderId);
            w.WriteString("transactionType", "BUY");
            w.WriteString("exchangeSegment", "NSE_EQ");
            w.WriteString("productType", "INTRADAY");
            w.WriteString("orderType", "LIMIT");
            w.WriteString("securityId", leg.SecurityId);
            w.WriteNumber("quantity", leg.Qty);
            w.WriteNumber("price", leg.Price);
            w.WriteNumber("targetPrice", super.Target);
            w.WriteNumber("stopLossPrice", super.StopLoss);
            w.WriteNumber("trailingJump", 0);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The change of a stop-loss limit leg: new trigger and limit together (UNVERIFIED).</summary>
    public static byte[] ModifyStopBody(DhanCredentials credentials, string brokerOrderId, int qty, decimal trigger, decimal limit)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("dhanClientId", credentials.ClientId);
            w.WriteString("orderId", brokerOrderId);
            w.WriteString("orderType", "STOP_LOSS");
            w.WriteString("validity", "DAY");
            w.WriteNumber("quantity", qty);
            w.WriteNumber("price", limit);
            w.WriteNumber("triggerPrice", trigger);
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
