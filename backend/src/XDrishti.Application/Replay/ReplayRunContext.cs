using XDrishti.Application.Research;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Replay;

/// <summary>What stays the same for every session of one run: the parameters, the one settings snapshot, the cost choice and sheet, the calendar and the last session the replay may read (the end of the range, never past the hold-out clamp).</summary>
/// <param name="Parameters">The parameters.</param>
/// <param name="Settings">The settings snapshot.</param>
/// <param name="Costs">What the simulator is asked for: gross, or the sheet with the starting capital.</param>
/// <param name="Sheet">The stored sheet; null when gross.</param>
/// <param name="Calendar">The trading calendar.</param>
/// <param name="LastAllowed">The last session that may be read.</param>
internal sealed record ReplayRunContext(ReplayParams Parameters, ReplaySettingsSnapshot Settings, CostChoice Costs, CostSheet? Sheet, TradingCalendar Calendar, DateOnly LastAllowed);
