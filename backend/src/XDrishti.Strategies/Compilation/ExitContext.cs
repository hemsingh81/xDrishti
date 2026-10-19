using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The facts from outside an exit that its table needs (spec P3-02b2, D11): the side (a structure trail reads <c>lowest</c> or <c>highest</c>), the style and the evaluation timeframe (the bound of <c>intraday_bars</c>,
/// which of the two time-stop fields is used) and the <c>max_hold_sessions</c> as written. The time rules are checked after the side, so only the raw value exists here; when it is absent or outside 1 to 5 the time rule's own
/// problem stands and the relation to <c>swing_sessions</c> is not checked.
/// </summary>
internal sealed record ExitContext(Side Side, Style Style, Timeframe EvaluationTimeframe, int? MaxHoldSessions);
