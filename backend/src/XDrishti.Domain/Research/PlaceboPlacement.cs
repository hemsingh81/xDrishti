
namespace XDrishti.Domain.Research;

/// <summary>Where a placebo shape puts a signal: the instrument to walk, how many sessions and minutes away from the real decision, and the side.</summary>
/// <param name="InstrumentId">The instrument the placebo trade is walked on.</param>
/// <param name="SessionOffset">Sessions of that instrument away from the real session (0 is the same session).</param>
/// <param name="MinuteOffset">Minutes after the real decision minute (never negative: an earlier entry would be look-ahead).</param>
/// <param name="Side">The side of the placebo trade.</param>
public sealed record PlaceboPlacement(Guid InstrumentId, int SessionOffset, int MinuteOffset, Side Side);
