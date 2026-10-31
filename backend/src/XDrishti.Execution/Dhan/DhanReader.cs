using System.Net.Http.Headers;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Dhan;

/// <summary>
/// The one GET machinery of the Dhan adapter (ADR 0170), shared by the sandbox client (<see cref="DhanOrderClient"/>) and the live readiness reads (<see cref="LiveBrokerFacts"/>, ADR 0172). It has no write, no body and no other verb: every address comes from
/// <see cref="ExecutionEndpoints.Build"/> for its environment (so in live only the two readiness GETs exist), the HTTP client has the <see cref="GuardHandler"/> in front of the socket, and the token travels only in the <c>access-token</c> header of one request.
/// A read is retried once after 250 ms on a fault or a non-2xx other than a refused token (401 or DH-901: <c>token_invalid</c> at once). With an admission function (the readiness reads) each request must be admitted first and every request and local refusal
/// writes one audit log line (event 6616: environment, call, path template, status, reason code; never a host, query, id, body, header, token, client id or address).
/// </summary>
internal sealed partial class DhanReader(HttpClient http, ExecEnvironment environment, ILogger logger, TimeProvider clock, TimeSpan readTimeout)
{
    private static readonly TimeSpan ReadRetryGap = TimeSpan.FromMilliseconds(250);

    public async Task<BrokerRead<T>> ReadAsync<T>(DhanCall call, string token, Func<string, BrokerRead<T>> parse, CancellationToken cancellationToken, Func<bool>? admit = null)
    {
        var uri = ExecutionEndpoints.Build(environment, call);
        if (uri is null)
        {
            return Refuse<T>(admit, call, "guard_refused");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (admit is not null && !admit())
            {
                return Refuse<T>(admit, call, "read_rate_limited");
            }

            using var deadline = new CancellationTokenSource(readTimeout, clock);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var status = 0;
            try
            {
                using var request = new HttpRequestMessage(ExecutionEndpoints.MethodOf(call), uri);
                request.Headers.Add(ExecutionEndpoints.AccessTokenHeader, token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
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
                await Task.Delay(ReadRetryGap, clock, cancellationToken).ConfigureAwait(false);
            }
        }

        return BrokerRead.Unreadable<T>("read_failed");
    }

    /// <summary>A local refusal with no request: audited like a request with status 0.</summary>
    public BrokerRead<T> Refuse<T>(Func<bool>? admit, DhanCall call, string reason) => Audited(admit, call, 0, BrokerRead.Unreadable<T>(reason));

    private BrokerRead<T> Audited<T>(Func<bool>? admit, DhanCall call, int status, BrokerRead<T> read)
    {
        Audit(admit, call, status, read.Readable ? "ok" : read.Reason ?? "unreadable");
        return read;
    }

    private void Audit(Func<bool>? admit, DhanCall call, int status, string reason)
    {
        if (admit is not null && logger.IsEnabled(LogLevel.Information))
        {
            var name = environment == ExecEnvironment.Live ? "live" : "sandbox";
            var path = ExecutionEndpoints.TemplateOf(call);
            LogRead(logger, name, call, path, status, reason);
        }
    }

    [LoggerMessage(EventId = 6616, Level = LogLevel.Information, Message = "Dhan {Environment} read {Call} GET {Path} answered {Status}: {Reason}")]
    private static partial void LogRead(ILogger logger, string environment, DhanCall call, string path, int status, string reason);
}
