using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Market.Bars;
using XDrishti.Infrastructure.Brokers;

namespace XDrishti.Infrastructure.Market.Bars;

/// <summary>
/// The 1-minute bars of the tracked instruments from Dhan's Data API (ADR 0016 §7): for each planned instrument it asks <see cref="DhanIntradayChartClient"/> for every
/// window of the plan, one request at a time (throttled), with the access token of the primary Data account. A refused token ends the fetch (<see cref="IntradayFailureKind.Unauthorized"/>);
/// any other trouble fails that instrument only. Each window's bars must pass the timestamp-alignment check (<see cref="IntradayAlignmentCheck"/>) — a suspected offset fails
/// the instrument and nothing is shifted — and a minute that has not ended yet is dropped (the current minute may still change). Rows, sessions, overlap and storing are the pipeline's.
/// Nothing secret is logged: instrument slugs and counts only.
/// </summary>
internal sealed partial class DhanIntradayBarSource(IDataAccountStatus account, DhanIntradayChartClient chart, TimeProvider clock, ILogger<DhanIntradayBarSource> logger) : IIntradayBarSource
{
    /// <summary>Whether the account is connected is the pipeline's question (<see cref="IDataAccountStatus"/>); the source itself can always try.</summary>
    public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(SourceAvailability.Ready);

    public async IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Targets.Count == 0)
        {
            yield break;
        }

        var token = await account.GetAccessTokenAsync(cancellationToken);
        if (token is null)
        {
            yield return new IntradayResult(plan.Targets[0].InstrumentId, [], 0, new IntradayFailure(IntradayFailureKind.Unauthorized, "The Data account has no access token that works"));
            yield break;
        }

        foreach (var target in plan.Targets)
        {
            var result = await FetchTargetAsync(token, target, cancellationToken);
            yield return result;
            if (result.Failure?.Kind == IntradayFailureKind.Unauthorized)
            {
                yield break;
            }
        }
    }

    private async ValueTask<IntradayResult> FetchTargetAsync(SecretValue token, IntradayTarget target, CancellationToken cancellationToken)
    {
        var bars = new List<Bar>();
        var requests = 0;
        foreach (var window in target.Windows)
        {
            var fetched = await chart.FetchAsync(token, target, window, cancellationToken);
            requests += fetched.Requests;
            var failure = fetched.Failure;
            if (failure is null && IntradayAlignmentCheck.Problem(fetched.Bars, window) is { } problem)
            {
                failure = new IntradayFailure(IntradayFailureKind.Failed, problem);
            }

            if (failure is not null)
            {
                Failed(logger, target.Symbol, requests, failure.Message);
                return new IntradayResult(target.InstrumentId, [], requests, failure);
            }

            bars.AddRange(fetched.Bars);
        }

        // The minute that is still running is not final: leave it for the next fetch.
        var lastEnded = clock.GetUtcNow().ToUnixTimeSeconds() - 60;
        bars.RemoveAll(b => b.EpochSeconds > lastEnded);
        Fetched(logger, target.Symbol, requests, bars.Count);
        return new IntradayResult(target.InstrumentId, bars, requests);
    }

    [LoggerMessage(EventId = 3131, Level = LogLevel.Information, Message = "Dhan chart data for {Symbol}: {Requests} requests, {Bars} bars")]
    private static partial void Fetched(ILogger logger, string symbol, int requests, int bars);

    [LoggerMessage(EventId = 3132, Level = LogLevel.Warning, Message = "Dhan chart data for {Symbol} failed after {Requests} requests: {Reason}")]
    private static partial void Failed(ILogger logger, string symbol, int requests, string reason);
}
