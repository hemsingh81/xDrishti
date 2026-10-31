using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// The Dhan order adapter for the sandbox (P12-02, ADR 0157). It cannot be built for any environment but sandbox. Writes are never retried; a read is retried once after 250 ms. The token travels only in the <c>access-token</c> header of one request;
/// nothing here logs a body, a header or an exception message. The HTTP client is created by <see cref="DhanSandboxRegistration"/> with redirects off and the <see cref="GuardHandler"/> in front of the socket.
/// </summary>
public sealed partial class DhanOrderClient : IBrokerOrders, IBrokerStops, IBrokerReads
{
    private static readonly TimeSpan ReadRetryGap = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient _http;
    private readonly IExecCredentials _credentials;
    private readonly ExecEnvironment _environment;
    private readonly ILogger<DhanOrderClient> _logger;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan> _writeTimeout;
    private readonly TimeSpan _readTimeout;

    public DhanOrderClient(HttpClient http, IExecCredentials credentials, ExecEnvironment environment, ILogger<DhanOrderClient> logger, TimeProvider clock, TimeSpan writeTimeout, TimeSpan readTimeout)
        : this(http, credentials, environment, logger, clock, () => writeTimeout, readTimeout)
    {
    }

    // The write timeout is read at every write, so a change of exec.send_timeout_s takes effect without a restart.
    public DhanOrderClient(HttpClient http, IExecCredentials credentials, ExecEnvironment environment, ILogger<DhanOrderClient> logger, TimeProvider clock, Func<TimeSpan> writeTimeout, TimeSpan readTimeout)
    {
        if (environment != ExecEnvironment.Sandbox)
        {
            throw new InvalidOperationException("The Dhan adapter is built for the sandbox environment only in this slice.");
        }

        _http = http;
        _credentials = credentials;
        _environment = environment;
        _logger = logger;
        _clock = clock;
        _writeTimeout = writeTimeout;
        _readTimeout = readTimeout;
    }

    public async Task<BrokerAck> PlaceAsync(BrokerLeg leg, CancellationToken cancellationToken)
    {
        if (DhanWire.Validate(leg) is { } invalid)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, DhanWire.InstructionInvalid + ":" + invalid);
        }

        if (_credentials.Current() is not { } credentials)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "execution.token_unavailable");
        }

        return leg.Super is null
            ? await WriteAsync(DhanCall.OrdersPost, null, credentials, DhanWire.PlaceBody(credentials, leg), cancellationToken).ConfigureAwait(false)
            : await WriteAsync(DhanCall.SuperOrdersPost, null, credentials, DhanWire.SuperPlaceBody(credentials, leg), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrokerAck> ModifyStopAsync(string clientOrderId, decimal trigger, decimal limit, CancellationToken cancellationToken)
    {
        if (trigger <= 0m || trigger % DhanWire.Tick != 0m || limit <= 0m || limit % DhanWire.Tick != 0m || limit > trigger
            || !ClientOrderId.TryParse(clientOrderId, out var id) || id.Kind != LegKind.Stop)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, DhanWire.InstructionInvalid + ":stop");
        }

        if (_credentials.Current() is not { } credentials)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "execution.token_unavailable");
        }

        View? view;
        try
        {
            view = await FindAsync(clientOrderId, credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerLookupFailedException)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "lookup_failed");
        }

        return view is null
            ? new BrokerAck(BrokerOutcome.NotSent, null, "order_not_found")
            : view.Side != ExecSide.Sell
                ? new BrokerAck(BrokerOutcome.NotSent, null, DhanWire.InstructionInvalid + ":stop_wrong_side")
                : await WriteAsync(DhanCall.OrderPut, view.OrderId, credentials, DhanWire.ModifyStopBody(credentials, view.OrderId, view.Qty, trigger, limit), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrokerAck> ModifyAsync(string clientOrderId, decimal price, CancellationToken cancellationToken)
    {
        if (price <= 0m || price % DhanWire.Tick != 0m)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, DhanWire.InstructionInvalid + ":price_or_tick");
        }

        if (ClientOrderId.TryParse(clientOrderId, out var parsed) && parsed.Kind == LegKind.Stop)
        {
            // A stop leg changes with its trigger (ModifyStopAsync); a plain LIMIT body would drop the trigger.
            return new BrokerAck(BrokerOutcome.NotSent, null, DhanWire.InstructionInvalid + ":use_modify_stop");
        }

        if (_credentials.Current() is not { } credentials)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "execution.token_unavailable");
        }

        View? view;
        try
        {
            view = await FindAsync(clientOrderId, credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerLookupFailedException)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "lookup_failed");
        }

        return view is null
            ? new BrokerAck(BrokerOutcome.NotSent, null, "order_not_found")
            : await WriteAsync(DhanCall.OrderPut, view.OrderId, credentials, DhanWire.ModifyBody(credentials, view.OrderId, view.Qty, price), cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrokerAck> CancelAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        if (_credentials.Current() is not { } credentials)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "execution.token_unavailable");
        }

        View? view;
        try
        {
            view = await FindAsync(clientOrderId, credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (BrokerLookupFailedException)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "lookup_failed");
        }

        if (view is null)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "order_not_found");
        }

        var ack = await WriteAsync(DhanCall.OrderDelete, view.OrderId, credentials, null, cancellationToken).ConfigureAwait(false);

        return ack;
    }

    public async Task<BrokerLeg?> LookupAsync(string clientOrderId, CancellationToken cancellationToken)
    {
        if (_credentials.Current() is not { } credentials)
        {
            throw new BrokerLookupFailedException("execution.token_unavailable");
        }

        var view = await FindAsync(clientOrderId, credentials, cancellationToken).ConfigureAwait(false);
        if (view is null)
        {
            return null;
        }

        return ClientOrderId.TryParse(clientOrderId, out var id)
            ? new BrokerLeg(clientOrderId, id.Kind, view.Side, view.SecurityId, view.Qty, view.Price)
            : throw new BrokerLookupFailedException("client_order_id");
    }

    public async Task<BrokerRead<IReadOnlyList<BrokerOrderView>>> GetOrderBookAsync(CancellationToken cancellationToken) =>
        await ReadAsync(DhanCall.OrdersGet, DhanReads.OrderBook, cancellationToken).ConfigureAwait(false);

    public async Task<BrokerRead<IReadOnlyList<BrokerTradeView>>> GetTradeBookAsync(CancellationToken cancellationToken) =>
        await ReadAsync(DhanCall.TradesGet, DhanReads.TradeBook, cancellationToken).ConfigureAwait(false);

    public async Task<BrokerRead<IReadOnlyList<BrokerPositionView>>> GetPositionsAsync(CancellationToken cancellationToken) =>
        await ReadAsync(DhanCall.PositionsGet, DhanReads.Positions, cancellationToken).ConfigureAwait(false);

    public async Task<BrokerRead<BrokerFundsView>> GetFundsAsync(CancellationToken cancellationToken) =>
        await ReadAsync(DhanCall.FundsGet, DhanReads.Funds, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The broker's profile (<c>GET /v2/profile</c>) for readiness (P12-11). Not on any public port: only <see cref="DhanBrokerFacts"/> calls it, through its own read limiter (<paramref name="admit"/>, asked before every request) and never through the send limiter.
    /// Every request and every local refusal writes one audit log line (call, path template, status, reason code).
    /// </summary>
    internal Task<BrokerRead<ProfileFacts>> GetProfileAsync(Func<bool> admit, CancellationToken cancellationToken) => ReadAsync(DhanCall.ProfileGet, DhanReads.Profile, cancellationToken, admit);

    /// <summary>The configured static IP (<c>GET /v2/ip/getIP</c>) for readiness (P12-11); as <see cref="GetProfileAsync(Func{bool}, CancellationToken)"/>.</summary>
    internal Task<BrokerRead<IpFacts>> GetStaticIpAsync(Func<bool> admit, CancellationToken cancellationToken) => ReadAsync(DhanCall.StaticIpGet, DhanReads.StaticIp, cancellationToken, admit);

    // One GET, retried once after 250 ms on a fault or a non-2xx other than a refused token (401 or DH-901: token_invalid at once, a second try with the same token cannot change it); the answer is parsed by the given reader. Nothing is written and
    // nothing is counted by the send limiter. With an admission function (the readiness reads) each request must be admitted by it first, and every request and refusal is audited in the log.
    private async Task<BrokerRead<T>> ReadAsync<T>(DhanCall call, Func<string, BrokerRead<T>> parse, CancellationToken cancellationToken, Func<bool>? admit = null)
    {
        if (_credentials.Current() is not { } credentials)
        {
            return Audited(admit, call, 0, BrokerRead.Unreadable<T>("execution.token_unavailable"));
        }

        var uri = ExecutionEndpoints.Build(_environment, call);
        if (uri is null)
        {
            return Audited(admit, call, 0, BrokerRead.Unreadable<T>("guard_refused"));
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (admit is not null && !admit())
            {
                return Audited(admit, call, 0, BrokerRead.Unreadable<T>("read_rate_limited"));
            }

            using var deadline = new CancellationTokenSource(_readTimeout, _clock);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var status = 0;
            try
            {
                using var request = Request(call, uri, credentials, null);
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                status = (int)response.StatusCode;
                var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return Audited(admit, call, status, parse(text));
                }

                if (DhanErrorMap.IsTokenRejected(response.StatusCode, text))
                {
                    return Audited(admit, call, status, BrokerRead.Unreadable<T>("token_invalid"));
                }

                Audit(admit, call, status, "read_failed");
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Audit(admit, call, status, "read_fault");
            }

            if (attempt == 0)
            {
                await Task.Delay(ReadRetryGap, _clock, cancellationToken).ConfigureAwait(false);
            }
        }

        return BrokerRead.Unreadable<T>("read_failed");
    }

    private BrokerRead<T> Audited<T>(Func<bool>? admit, DhanCall call, int status, BrokerRead<T> read)
    {
        Audit(admit, call, status, read.Readable ? "ok" : read.Reason ?? "unreadable");
        return read;
    }

    // Only the reads that come with an admission function (the readiness reads) are audited: the call, the path template from the guard table, the HTTP status (0 when no answer) and a reason code. Never a body, header, token, client id, query or id value.
    private void Audit(Func<bool>? admit, DhanCall call, int status, string reason)
    {
        if (admit is not null && _logger.IsEnabled(LogLevel.Information))
        {
            var path = ExecutionEndpoints.TemplateOf(call);
            LogRead(_logger, call, path, status, reason);
        }
    }

    private sealed record View(string OrderId, ExecSide Side, string SecurityId, int Qty, decimal Price);

    private async Task<BrokerAck> WriteAsync(DhanCall call, string? id, DhanCredentials credentials, byte[]? body, CancellationToken cancellationToken)
    {
        var uri = ExecutionEndpoints.Build(_environment, call, id);
        if (uri is null)
        {
            return new BrokerAck(BrokerOutcome.NotSent, null, "guard_refused");
        }

        using var deadline = new CancellationTokenSource(_writeTimeout(), _clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var request = Request(call, uri, credentials, body);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var ack = call == DhanCall.OrderDelete || call == DhanCall.OrderPut ? DhanErrorMap.FromResponse(response.StatusCode, Normalise(call, text)) : DhanErrorMap.FromResponse(response.StatusCode, text);
            LogOutcome(_logger, call, (int)response.StatusCode, ack.Outcome, ack.Reason);
            return ack;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            var ack = DhanErrorMap.FromFault(e);
            LogOutcome(_logger, call, 0, ack.Outcome, ack.Reason);
            return ack;
        }
    }

    // A modify answers a status such as TRANSIT or PENDING; a cancel answers CANCELLED. Both are an acknowledgement of that request, so the status word is mapped to PENDING for the shared rule; anything else stays as it is.
    private static string Normalise(DhanCall call, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var status = DhanErrorMap.Str(doc.RootElement, "orderStatus");
            var id = DhanErrorMap.Str(doc.RootElement, "orderId");
            return call == DhanCall.OrderDelete && string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(id)
                ? Acknowledged(id)
                : text;
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string Acknowledged(string id)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("orderId", id);
            w.WriteString("orderStatus", "PENDING");
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<View?> FindAsync(string clientOrderId, DhanCredentials credentials, CancellationToken cancellationToken)
    {
        var uri = ExecutionEndpoints.Build(_environment, DhanCall.OrderByCorrelationGet, clientOrderId) ?? throw new BrokerLookupFailedException("guard_refused");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deadline = new CancellationTokenSource(_readTimeout, _clock);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                using var request = Request(DhanCall.OrderByCorrelationGet, uri, credentials, null);
                using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Absence only when the body is a Dhan error that says the order was not found; any other 404 (wrong route or host) is a failed lookup. The documented shape is UNVERIFIED (exec.lookup_verified gates resends).
                    return IsOrderNotFound(text) ? null : throw new BrokerLookupFailedException("lookup_404_unrecognised");
                }

                if (response.IsSuccessStatusCode)
                {
                    return ParseView(text, clientOrderId);
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (attempt == 0)
            {
                await Task.Delay(ReadRetryGap, _clock, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new BrokerLookupFailedException("lookup_unreadable");
    }

    private static bool IsOrderNotFound(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return DhanErrorMap.Str(doc.RootElement, "errorCode") is not null && DhanErrorMap.Str(doc.RootElement, "errorMessage") is { } m && m.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>An empty array or object is absence; an object (or the first of an array) with an orderId is the order; anything else cannot be read and is a failed lookup, never absence. The field names are from Dhan's order page (UNVERIFIED against the sandbox).</summary>
    private static View? ParseView(string text, string clientOrderId)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    return null;
                }

                root = root[0];
            }

            if (root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().Any())
            {
                return null;
            }

            if (!string.Equals(DhanErrorMap.Str(root, "correlationId"), clientOrderId, StringComparison.Ordinal))
            {
                throw new BrokerLookupFailedException("correlation_mismatch");
            }

            var id = DhanErrorMap.Str(root, "orderId");
            var side = DhanErrorMap.Str(root, "transactionType");
            var security = DhanErrorMap.Str(root, "securityId");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(security) || side is null
                || !int.TryParse(DhanErrorMap.Str(root, "quantity"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var qty)
                || !decimal.TryParse(DhanErrorMap.Str(root, "price"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var price))
            {
                throw new BrokerLookupFailedException("lookup_unreadable");
            }

            return new View(id, string.Equals(side, "BUY", StringComparison.OrdinalIgnoreCase) ? ExecSide.Buy : ExecSide.Sell, security, qty, price);
        }
        catch (JsonException)
        {
            throw new BrokerLookupFailedException("lookup_unreadable");
        }
    }

    private static HttpRequestMessage Request(DhanCall call, Uri uri, DhanCredentials credentials, byte[]? body)
    {
        var request = new HttpRequestMessage(ExecutionEndpoints.MethodOf(call), uri);
        request.Headers.Add(ExecutionEndpoints.AccessTokenHeader, credentials.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    [LoggerMessage(EventId = 6615, Level = LogLevel.Information, Message = "Dhan sandbox call {Call} answered {Status}: {Outcome} {Reason}")]
    private static partial void LogOutcome(ILogger logger, DhanCall call, int status, BrokerOutcome outcome, string? reason);

    [LoggerMessage(EventId = 6616, Level = LogLevel.Information, Message = "Dhan sandbox read {Call} GET {Path} answered {Status}: {Reason}")]
    private static partial void LogRead(ILogger logger, DhanCall call, string path, int status, string reason);
}
