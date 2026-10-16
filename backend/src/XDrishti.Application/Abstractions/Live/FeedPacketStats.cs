using System.Globalization;
using System.Text.Json.Serialization;

namespace XDrishti.Application.Abstractions.Live;

/// <summary>Why a message of the feed was not used. Counted per response code, never thrown.</summary>
public enum FeedDropReason
{
    /// <summary>Not dropped: the message was decoded.</summary>
    None,

    /// <summary>Shorter than the 8-byte header (truncated).</summary>
    TooShort,

    UnknownCode,

    /// <summary>The length of the message, or the length field in its header, is not what the code needs.</summary>
    LengthMismatch,

    UnknownSegment,

    BadPrice,

    BadTime,

    /// <summary>A text message (the feed sends binary packets).</summary>
    NotBinary,

    /// <summary>Longer than any packet can be.</summary>
    Oversized,

    /// <summary>Decoded, but the security is not in the active set (reclassified after the decode).</summary>
    UnknownInstrument,
}

/// <summary>What was seen for one response code in a window: how many packets were decoded and dropped (and why), the length of the first and of the latest <b>frame</b> (WebSocket message) they came in, the length the header of the first packet declared and how many of the packets came in a frame that held several (Dhan packs packets back to back, observed 2026-10-14). Counts and lengths only.</summary>
public sealed record PacketCodeSummary(
    long Decoded,
    long Dropped,
    IReadOnlyDictionary<string, long> Reasons,
    int FirstLength,
    int LastLength,
    int? FirstDeclaredLength,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long InMultiPacketFrames = 0);

/// <summary>One group of dropped messages for the log: the response code (<c>none</c> for a message without one), the reason, how many and the length of the latest.</summary>
public sealed record DroppedPacketGroup(string Code, FeedDropReason Reason, long Count, int LastLength);

/// <summary>
/// Counts every message of the feed by response code (byte 0) and outcome, with the lengths that show a wrong assumption about the layout (P2-10 observability). Cardinality is fixed: 256 codes plus one cell for messages without a code (a
/// text message, an oversized one) times the <see cref="FeedDropReason"/> values. Thread safe (the receive loop writes, the heartbeat and the log read). <b>Only codes, lengths and counts are kept - never the bytes of a message.</b>
/// </summary>
public sealed class FeedPacketStats
{
    private const int NoCodeCell = 256;

    private static readonly int ReasonCount = Enum.GetValues<FeedDropReason>().Length;

    private readonly object _gate = new();
    private readonly Cell?[] _cells = new Cell?[NoCodeCell + 1];

    /// <summary>The name of a reason in the heartbeat and the log (camel case).</summary>
    public static string ReasonName(FeedDropReason reason) => char.ToLowerInvariant(reason.ToString()[0]) + reason.ToString()[1..];

    /// <summary>Counts one packet. <paramref name="code"/> is its byte 0 (null when there is none); <paramref name="length"/> the length of the whole frame it came in; <paramref name="declaredLength"/> the length field of its header when there were bytes enough for one.</summary>
    public void Record(int? code, int length, int? declaredLength, FeedDropReason reason, bool inMultiPacketFrame = false)
    {
        lock (_gate)
        {
            var cell = CellOf(code);
            if (cell.FirstLength < 0)
            {
                cell.FirstLength = length;
                cell.FirstDeclared = declaredLength;
            }

            cell.LastLength = length;
            if (inMultiPacketFrame)
            {
                cell.InMultiPacketFrames++;
            }

            if (reason == FeedDropReason.None)
            {
                cell.Decoded++;
            }
            else
            {
                cell.Dropped++;
                cell.Reasons[(int)reason]++;
            }
        }
    }

    /// <summary>A message that was decoded is dropped after all (its security is not in the set): moves one from decoded to dropped.</summary>
    public void Reclassify(int code, FeedDropReason reason)
    {
        lock (_gate)
        {
            var cell = CellOf(code);
            if (cell.Decoded > 0)
            {
                cell.Decoded--;
            }

            cell.Dropped++;
            cell.Reasons[(int)reason]++;
        }
    }

    /// <summary>Forgets everything (the start of a window).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_cells);
        }
    }

    /// <summary>Messages dropped so far, all codes.</summary>
    public long TotalDropped
    {
        get
        {
            lock (_gate)
            {
                return _cells.Sum(c => c?.Dropped ?? 0);
            }
        }
    }

    /// <summary>The codes seen, the busiest first, at most <paramref name="maxCodes"/> of them (keys are the code as text, <c>none</c> for messages without one). Empty when nothing was received.</summary>
    public IReadOnlyDictionary<string, PacketCodeSummary> Summary(int maxCodes)
    {
        lock (_gate)
        {
            return _cells
                .Select((cell, index) => (cell, index))
                .Where(x => x.cell is not null)
                .OrderByDescending(x => x.cell!.Decoded + x.cell.Dropped)
                .ThenBy(x => x.index)
                .Take(maxCodes)
                .ToDictionary(
                    x => Key(x.index),
                    x => new PacketCodeSummary(
                        x.cell!.Decoded,
                        x.cell.Dropped,
                        x.cell.Reasons.Select((count, reason) => (count, reason)).Where(r => r.count > 0).ToDictionary(r => ReasonName((FeedDropReason)r.reason), r => r.count),
                        x.cell.FirstLength,
                        x.cell.LastLength,
                        x.cell.FirstDeclared,
                        x.cell.InMultiPacketFrames));
        }
    }

    /// <summary>The largest groups of dropped messages (code and reason), biggest first.</summary>
    public IReadOnlyList<DroppedPacketGroup> TopDropped(int count)
    {
        lock (_gate)
        {
            return [.. _cells
                .Select((cell, index) => (cell, index))
                .Where(x => x.cell is not null)
                .SelectMany(x => x.cell!.Reasons.Select((n, reason) => (n, reason)).Where(r => r.n > 0).Select(r => new DroppedPacketGroup(Key(x.index), (FeedDropReason)r.reason, r.n, x.cell.LastLength)))
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Code, StringComparer.Ordinal)
                .Take(count)];
        }
    }

    private static string Key(int index) => index == NoCodeCell ? "none" : index.ToString(CultureInfo.InvariantCulture);

    private Cell CellOf(int? code)
    {
        var index = code is >= 0 and < NoCodeCell ? code.Value : NoCodeCell;
        return _cells[index] ??= new Cell();
    }

    private sealed class Cell
    {
        public long Decoded { get; set; }

        public long Dropped { get; set; }

        public long[] Reasons { get; } = new long[ReasonCount];

        public int FirstLength { get; set; } = -1;

        public int LastLength { get; set; }

        public int? FirstDeclared { get; set; }

        public long InMultiPacketFrames { get; set; }
    }
}
