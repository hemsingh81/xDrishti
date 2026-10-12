using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>A span of time to fetch, at most 90 days. Neighbouring windows of a plan share their boundary instant (the merge removes the duplicate minute).</summary>
public readonly record struct FetchWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>An instrument to fetch with the windows to ask for, oldest first.</summary>
public sealed record IntradayTarget(Guid InstrumentId, string Symbol, string Type, string SecurityId, string Segment, IReadOnlyList<FetchWindow> Windows);

/// <summary>What to fetch; produced by the pipeline (<c>DhanFetchPlanner</c>), carried out by an <see cref="IIntradayBarSource"/>.</summary>
public sealed record IntradayFetchPlan(IReadOnlyList<IntradayTarget> Targets);

public enum IntradayFailureKind
{
    /// <summary>This instrument could not be fetched; the others go on.</summary>
    Failed,

    /// <summary>The broker refused the access token: the phase ends (<c>data.dhan_not_connected</c>).</summary>
    Unauthorized,
}

public sealed record IntradayFailure(IntradayFailureKind Kind, string Message);

/// <summary>What one instrument yielded: its 1-minute bars (all windows together, any order, duplicates allowed) or the reason it failed. <see cref="Requests"/> counts the HTTP requests made.</summary>
public sealed record IntradayResult(Guid InstrumentId, IReadOnlyList<Bar> Bars, int Requests, IntradayFailure? Failure = null);

/// <summary>Whether the source can fetch at all, and if not, why (shown to the owner).</summary>
public sealed record SourceAvailability(bool Available, string? Reason)
{
    public static SourceAvailability Ready { get; } = new(true, null);
}

/// <summary>
/// 1-minute bars from the broker's Data API (ADR 0016 §7). The implementation owns the HTTP, the access token and the throttle
/// (<c>RequestThrottle</c>, <c>Data:Dhan:RequestsPerSecond</c>); it never logs the token or the broker's raw answer. Everything after the fetch (checks, precedence,
/// storing, aggregating) is the pipeline's, so every source behaves the same.
/// </summary>
public interface IIntradayBarSource
{
    ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Fetches the targets one after the other and yields each as soon as it is done (that is the progress). A failure of one instrument is a result, not an exception.</summary>
    IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, CancellationToken cancellationToken);
}
