namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// One stored bar of a rolling series (<c>options.rolling_bars</c>), prices in paise as stored: the offset it was taken at, the strike it had at that bar, the underlying's price, the four prices, volume, open interest and the vendor's IV (null when not sent,
/// never zero). <see cref="Suspect"/> is the stored flag; <see cref="Usable"/> says the bar's key-session passed the archive quality checks (P14-04): the reader sets it false and the use case sets it from the stored verdicts.
/// </summary>
public sealed record OptionBar(DateTimeOffset Ts, int Offset, int StrikePx, int SpotPx, int OpenPx, int HighPx, int LowPx, int ClosePx, long? Volume, long? Oi, float? Iv, bool Suspect, bool Usable);
