namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>
/// One complete 1-minute bar of the opening minutes (BK-03b), built from exchange time. <see cref="MinuteStart"/> is the start of the IST minute; <see cref="Volume"/> is the volume traded in the minute (cumulative day volume at the last packet in the minute minus that
/// at the last packet before it); <see cref="Packets"/> is how many packets built it.
/// </summary>
public sealed record MinuteBar(Guid InstrumentId, DateTimeOffset MinuteStart, decimal Open, decimal High, decimal Low, decimal Close, long Volume, int Packets);
