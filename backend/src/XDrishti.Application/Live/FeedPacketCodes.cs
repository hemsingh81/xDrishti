using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>The response codes of the packets this application uses (the first byte of a message).</summary>
internal static class FeedPacketCodes
{
    public static int Of(FeedPacketKind kind) => kind switch
    {
        FeedPacketKind.Ticker => 2,
        FeedPacketKind.Quote => 4,
        FeedPacketKind.PreviousClose => 6,
        _ => 50,
    };
}
