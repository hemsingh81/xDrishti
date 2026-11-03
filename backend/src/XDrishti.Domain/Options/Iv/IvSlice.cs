using XDrishti.Domain.Options.Remark;

namespace XDrishti.Domain.Options.Iv;

/// <summary>The bars of one side of one series at one timestamp, by offset (P14-11).</summary>
public sealed class IvSlice(DateTimeOffset ts, IReadOnlyDictionary<int, OptionBar> byOffset)
{
    /// <summary>The bar start.</summary>
    public DateTimeOffset Ts { get; } = ts;

    /// <summary>The bar at the offset, or null when the series has none at this timestamp.</summary>
    public OptionBar? At(int offset) => byOffset.GetValueOrDefault(offset);
}
