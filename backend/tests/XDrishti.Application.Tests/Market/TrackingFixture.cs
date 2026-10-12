using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Tracking;

namespace XDrishti.Application.Tests.Market;

/// <summary>Builds the tracking service and handlers on in-memory ports and a controllable clock.</summary>
internal sealed class TrackingFixture
{
    public const string Owner = "owner@example.invalid";

    public FakeTimeProvider Clock { get; } = new(MasterFixtures.Now);

    public InMemoryTrackingData Data { get; } = new();

    public InstrumentTracking Tracking => new(Data, Data);

    public TrackInstrumentsHandler Track => new(Tracking, Data, Data, Clock, NullLogger<TrackInstrumentsHandler>.Instance);

    public UntrackInstrumentsHandler Untrack => new(Tracking, Data, Data, Clock, NullLogger<UntrackInstrumentsHandler>.Instance);

    public ResolveInstrumentsHandler Resolve => new(Data);

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<TrackResult> TrackOkAsync(params Guid[] ids)
    {
        var result = await Track.HandleAsync(new TrackInstrumentsCommand(new TrackingInput(ids), Owner), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }
}
