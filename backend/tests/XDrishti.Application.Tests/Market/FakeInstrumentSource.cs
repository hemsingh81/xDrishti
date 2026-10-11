using XDrishti.Application.Abstractions.Market;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Tests.Market;

/// <summary>Serves the committed samples, or whatever the test configures, as the two source files.</summary>
internal sealed class FakeInstrumentSource : IInstrumentSource
{
    public static readonly SourceStamp DhanStamp = new("\"dhan-etag-1\"", "Sat, 10 Oct 2026 02:50:08 GMT");

    public static readonly SourceStamp SectorStamp = new("W/\"sector-etag-1\"", "Sat, 10 Oct 2026 03:31:52 GMT");

    public Func<SourceStamp?, SourceDownload> Master { get; set; } = known =>
        known is { IsEmpty: false } && known.ETag == DhanStamp.ETag
            ? SourceDownload.NotModified(DhanStamp)
            : SourceDownload.WithContent(MasterFixtures.Open(MasterFixtures.Master), DhanStamp);

    public Func<SourceDownload> Sectors { get; set; } = () => SourceDownload.WithContent(MasterFixtures.Open(MasterFixtures.Sectors), SectorStamp);

    public List<SourceStamp?> MasterRequests { get; } = [];

    public int SectorRequests { get; private set; }

    public ValueTask<SourceDownload> OpenMasterAsync(SourceStamp? known, CancellationToken cancellationToken)
    {
        MasterRequests.Add(known);
        return ValueTask.FromResult(Master(known));
    }

    public ValueTask<SourceDownload> OpenSectorsAsync(CancellationToken cancellationToken)
    {
        SectorRequests++;
        return ValueTask.FromResult(Sectors());
    }

    public static SourceDownload Content(string text, SourceStamp? stamp = null) => SourceDownload.WithContent(MasterFixtures.AsStream(text), stamp ?? DhanStamp);
}
