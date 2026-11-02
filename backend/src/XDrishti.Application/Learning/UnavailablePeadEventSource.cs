using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The PEAD event source before the PEAD strategy is built (P15-09): there are no events, so protocol P answers NOT_EVALUABLE with the seasons named. The strategy's slice registers the real one in its place.</summary>
internal sealed class UnavailablePeadEventSource : IPeadEventSource
{
    public ValueTask<PeadEventSet> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new PeadEventSet([], "no PEAD events: the PEAD strategy (P15-09) stores none yet"));
}
