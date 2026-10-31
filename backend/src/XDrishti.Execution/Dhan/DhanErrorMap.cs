using System.Net;
using System.Text.Json;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// The three-way outcome of one write. Acknowledged: 2xx with a non-empty orderId and status TRANSIT or PENDING. NotSent: the request certainly did not create an order (failure before the body was written, a 2xx REJECTED, or a 4xx with a parseable Dhan error body).
/// Everything else is Unknown, and Unknown is only ever resolved by a lookup.
/// </summary>
public static class DhanErrorMap
{
    public static BrokerAck FromResponse(HttpStatusCode status, string body)
    {
        var code = (int)status;
        if (code is >= 200 and < 300)
        {
            return FromSuccess(body);
        }

        if (code is >= 400 and < 500 && ParseError(body) is { } error)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, Reason(error.Code, error.Message));
        }

        return new BrokerAck(BrokerOutcome.Unknown, null, "unreadable_or_server_error");
    }

    /// <summary>A failure to send. Only name resolution and the TLS handshake provably precede any byte of the request, so only they are NotSent (a connection error can be a reset after the body was written); a timeout or any other fault is Unknown.</summary>
    public static BrokerAck FromFault(Exception exception) => exception is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError }
        ? new BrokerAck(BrokerOutcome.NotSent, null, "not_connected")
        : new BrokerAck(BrokerOutcome.Unknown, null, "fault_after_send");

    private static BrokerAck FromSuccess(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = Str(root, "orderStatus");
            if (string.Equals(status, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                return new BrokerAck(BrokerOutcome.NotSent, null, "broker_rejected");
            }

            var id = Str(root, "orderId");
            return !string.IsNullOrEmpty(id) && status is not null && (status.Equals("TRANSIT", StringComparison.OrdinalIgnoreCase) || status.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                ? new BrokerAck(BrokerOutcome.Acknowledged, id, status.ToUpperInvariant())
                : new BrokerAck(BrokerOutcome.Unknown, null, "ack_unreadable");
        }
        catch (JsonException)
        {
            return new BrokerAck(BrokerOutcome.Unknown, null, "ack_unreadable");
        }
    }

    /// <summary>Whether a non-2xx answer to a read says the token was refused: HTTP 401, or a 4xx with a Dhan error body whose code is DH-901. Such an answer is final; another try with the same token cannot change it.</summary>
    internal static bool IsTokenRejected(HttpStatusCode status, string body) =>
        status == HttpStatusCode.Unauthorized || ((int)status is >= 400 and < 500 && ParseError(body) is { } error && string.Equals(error.Code, "DH-901", StringComparison.OrdinalIgnoreCase));

    private static (string Code, string Message)? ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var code = Str(doc.RootElement, "errorCode");
            var type = Str(doc.RootElement, "errorType");
            var message = Str(doc.RootElement, "errorMessage");
            return code is null || type is null || message is null ? null : (code, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Reason(string code, string message) => code.ToUpperInvariant() switch
    {
        "DH-901" => "token_invalid",
        "DH-904" => "rate_limited_by_broker",
        "DH-905" when message.Contains("IP", StringComparison.OrdinalIgnoreCase) => "ip_not_allowed",
        "DH-905" => "input_exception",
        "DH-906" => "broker_rejected",
        "DH-908" or "DH-909" or "DH-910" => "broker_error",
        _ => "broker_error",
    };

    internal static string? Str(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ValueKind is JsonValueKind.Number ? p.Value.GetRawText() : null;
            }
        }

        return null;
    }
}
