using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// The lag rules (spec P3-02, <i>Meaning</i> and <i>Lint rules</i>). <b>History.</b> A feature is read at its own lag (<c>close[2]</c>) and, inside <c>crosses_*</c> (lags 0 and 1) and <c>rising/falling</c> (lags 0 to n),
/// at that many more closed bars back: the lags add up, so <c>rising(close[200], 100)</c> reads 300 bars back. The frame keeps <see cref="FrameOptions.MaxHistoryDepth"/> values (lags 0 to 255), so a condition whose
/// largest lag plus 1 is above it is <c>lag_too_deep</c>; the largest lag plus 1 is the history depth the plan asks for. <b>Clocks.</b> A lagged evaluation reads "the previous value" of each feature on its own
/// clock; if the features of one <c>crosses_*</c>, <c>rising</c> or <c>falling</c> run on different clocks the previous values would be different moments, so it is <c>mixed_timeframes_in_lag</c>. A clock is a bar
/// timeframe (<c>close</c> on 15m: the previous 15-minute bar) or <b>the date</b>: the calendar and context features (<see cref="IndicatorInfo.DateDriven"/>) are read on 1m whatever the strategy runs on, but change
/// once a date and their previous value is the previous <i>date</i>, never the previous minute. So they are a clock of their own on every evaluation timeframe, 1m included: they may be lagged among themselves and
/// against constants, never against a bar feature (ADR 0034, Amendment 4). <b>Sessions.</b> A lagged read is by position, not by session: the frame's history runs on across sessions, so on the first bars of a
/// session <c>vwap[1]</c> is the last value of the previous session. Not an error — a rolling average is meant to run on — but for a feature that starts again at the session open and has a value at once
/// (<see cref="IndicatorInfo.HasValueAtSessionOpen"/>) it compares two sessions, so it is the warning <c>lag_across_session</c> with the <c>time</c> guard that clears it (ADR 0034, Amendment 19).
/// </summary>
internal static class LagRules
{
    /// <summary>The history depth the plan needs: the largest lag read plus 1, at least 1 (only meaningful when no error was found).</summary>
    public static int Apply(IBoundExpression root, BindingScope scope)
    {
        var walk = new Walk(scope);
        walk.Visit(root, 0, null);
        walk.ReportSessionReads(TimeGuard(root));
        return (int)Math.Min(Math.Max(walk.Deepest + 1, 1), int.MaxValue);
    }

    /// <summary>
    /// The first minute of the day from which a condition lets anything through because of a <c>time</c> check it must pass: the largest first-true minute among the conjuncts <c>time &gt;= "HH:MM"</c> and
    /// <c>time &gt; "HH:MM"</c> of its top-level <c>and</c> chain (the check may stand anywhere in the chain, but not under an <c>or</c> or a <c>not</c>, where it need not hold); null when there is none.
    /// </summary>
    private static int? TimeGuard(IBoundExpression node) => node switch
    {
        BoundLogical { Operator: BinaryOperator.And } both => Later(TimeGuard(both.Left), TimeGuard(both.Right)),
        BoundTimeCompare { Operator: BinaryOperator.GreaterOrEqual } compare => compare.MinuteOfDay,
        BoundTimeCompare { Operator: BinaryOperator.Greater } compare => compare.MinuteOfDay + 1,
        _ => null,
    };

    private static int? Later(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    /// <summary>What one call (or lone subscripted feature) reads of per-session features: which, how far back, and from which minute after the open the reads stay in the session.</summary>
    private sealed class SessionRead(IBoundExpression owner)
    {
        public IBoundExpression Owner { get; } = owner;

        public List<string> Names { get; } = [];

        public long Deepest { get; set; }

        /// <summary>Minutes after the open from which every read stays inside the session; null when some read cannot.</summary>
        public int? CleanFrom { get; set; }

        public int Reads { get; set; }
    }

    private sealed class Walk(BindingScope scope)
    {
        private const int MaxDepth = FrameOptions.MaxHistoryDepth;

        /// <summary>How the message names the clock of the calendar and context features.</summary>
        private const string DateClock = "the date (calendar and context features)";

        private readonly HashSet<IBoundExpression> _reported = new(ReferenceEqualityComparer.Instance);

        /// <summary>The lagged reads of per-session features, one entry for each call (or lone subscripted feature) that has any, in the order they were met.</summary>
        private readonly List<SessionRead> _sessionReads = [];

        private readonly Dictionary<IBoundExpression, SessionRead> _sessionReadOf = new(ReferenceEqualityComparer.Instance);

        /// <summary>The largest lag at which any feature is read.</summary>
        public long Deepest { get; private set; }

        /// <summary>Visits a node read <paramref name="offset"/> closed bars further back than written, inside the lagged call <paramref name="lagged"/> (if any).</summary>
        public void Visit(IBoundExpression node, long offset, IBoundExpression? lagged)
        {
            switch (node)
            {
                case BoundFeature feature:
                    VisitFeature(feature, offset, lagged);
                    break;
                case BoundCrossing crossing:
                    CheckMixed(crossing, crossing.Left, crossing.Right);
                    Visit(crossing.Left, offset + 1, crossing);
                    Visit(crossing.Right, offset + 1, crossing);
                    break;
                case BoundRun run:
                    if (run.Count + 1L > MaxDepth && _reported.Add(run))
                    {
                        ReportCall(run, run.Count);
                    }

                    CheckMixed(run, run.Operand);
                    Visit(run.Operand, offset + run.Count, run);
                    break;
                case BoundInvalid { Lagged: { } call, Parts: { } parts }:
                    // a crosses_*, rising or falling that could not be bound: its operands are still read further back and still on their own clocks
                    CheckMixed(node, [.. parts.Take(call.Operands)]);
                    for (var i = 0; i < parts.Count; i++)
                    {
                        var isOperand = i < call.Operands;
                        Visit(parts[i], isOperand ? offset + call.Lag : offset, isOperand ? node : lagged);
                    }

                    break;
                default:
                    foreach (var child in BoundTree.Children(node))
                    {
                        Visit(child, offset, lagged);
                    }

                    break;
            }
        }

        private void VisitFeature(BoundFeature feature, long offset, IBoundExpression? lagged)
        {
            var lag = feature.Lag + offset;
            Deepest = Math.Max(Deepest, lag);
            if (feature.Lag + 1L > MaxDepth)
            {
                scope.Error(
                    LintCodes.LagTooDeep,
                    scope.Quoted(feature.Span) + " needs a history of " + (feature.Lag + 1L) + " closed bars, but the frame keeps at most " + MaxDepth + " (lags 0 to " + (MaxDepth - 1) + ").",
                    feature.Span);
            }
            else if (lag + 1 > MaxDepth && lagged is not null && _reported.Add(lagged))
            {
                ReportCall(lagged, lag);
            }

            NoteSessionRead(feature, lag, lagged ?? feature);
        }

        /// <summary>
        /// Remembers a read <paramref name="lag"/> closed bars back of a feature that starts again at the session open and has a value on the first bar, when some evaluation can fall in the first bars of a session:
        /// the strategy runs on an intraday timeframe, and its first evaluation (at the close of its first bar, <c>E</c> minutes after the open) comes before the bar <c>(lag + 1) · T</c> minutes after the open from which
        /// a read <c>lag</c> bars of the feature's own timeframe <c>T</c> back stays inside the session. A strategy on 1D or longer evaluates at the end of a session and is not affected.
        /// </summary>
        private void NoteSessionRead(BoundFeature feature, long lag, IBoundExpression owner)
        {
            var barMinutes = feature.Spec.Timeframe.Minutes();
            var evaluationMinutes = scope.EvaluationTimeframe.Minutes();
            if (lag < 1 || barMinutes == 0 || evaluationMinutes == 0 || evaluationMinutes >= (lag + 1) * barMinutes
                || !IndicatorCatalog.TryGet(feature.Spec.Name, out var info) || !info.HasValueAtSessionOpen(feature.Spec))
            {
                return;
            }

            if (!_sessionReadOf.TryGetValue(owner, out var read))
            {
                read = new SessionRead(owner);
                _sessionReadOf.Add(owner, read);
                _sessionReads.Add(read);
            }

            // the feature as written, without its subscript
            var text = scope.Text(feature.Span);
            var bracket = text.LastIndexOf('[');
            var name = bracket > 0 ? text[..bracket] : text;
            if (!read.Names.Contains(name))
            {
                read.Names.Add(name);
            }

            read.Deepest = Math.Max(read.Deepest, lag);

            // the minutes after the open from which this read stays inside the session: none when no bar of the session is lag bars from its open (the last bar, which may be a short one, is the farthest)
            var cleanFrom = lag * barMinutes >= SessionClock.RegularBars ? (int?)null : (int)Math.Min((lag + 1) * barMinutes, SessionClock.RegularBars);
            read.CleanFrom = read.Reads == 0 ? cleanFrom : read.CleanFrom is { } known && cleanFrom is { } other ? Math.Max(known, other) : null;
            read.Reads++;
        }

        /// <summary>
        /// Warns about every call (or lone subscripted feature) that reads a per-session feature across a session open, unless the condition has a <c>time</c> check (<paramref name="guard"/>, the first minute of the
        /// day it lets through) that keeps every evaluation of the strategy out of the bars concerned: the first evaluation the check lets through, a close of the strategy's timeframe, is at or after the minute from
        /// which the reads stay inside the session.
        /// </summary>
        public void ReportSessionReads(int? guard)
        {
            var evaluationMinutes = scope.EvaluationTimeframe.Minutes();
            foreach (var read in _sessionReads)
            {
                if (guard is { } from && read.CleanFrom is { } clean)
                {
                    var sinceOpen = Math.Max(from - SessionClock.OpenMinute, 1);
                    if ((sinceOpen + evaluationMinutes - 1) / evaluationMinutes * evaluationMinutes >= clean)
                    {
                        continue;
                    }
                }

                var names = string.Join(", ", read.Names.Select(BindingScope.Quote));
                var back = read.Deepest == 1 ? "one closed bar back" : "up to " + read.Deepest.ToString(CultureInfo.InvariantCulture) + " closed bars back";
                var cure = read.CleanFrom is { } minutes
                    ? "Wait until the session has bars of its own, for example with `and time >= \"" + Clock(SessionClock.OpenMinute + minutes) + "\"`."
                    : "No bar of a session is far enough from its open for a read that far back; read fewer bars back.";
                scope.Warning(
                    LintCodes.LagAcrossSession,
                    scope.Quoted(read.Owner.Span) + " reads " + names + " " + back + ", and on the first bars of every session that is a value of the previous session: a lagged read does not stop at the session open, "
                        + "where the feature starts again. " + cure,
                    read.Owner.Span);
            }
        }

        private static string Clock(int minuteOfDay) => string.Create(CultureInfo.InvariantCulture, $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}");

        private void ReportCall(IBoundExpression call, long lag) =>
            scope.Error(
                LintCodes.LagTooDeep,
                scope.Quoted(call.Span) + " reads values up to " + lag + " closed bars back (the lags add up), so it needs a history of " + (lag + 1) + "; the frame keeps at most " + MaxDepth + " (lags 0 to " + (MaxDepth - 1) + ").",
                call.Span);

        /// <summary>The features under the operands of a lagged call must all run on one clock: the same bar timeframe, or all of them features of the date.</summary>
        private void CheckMixed(IBoundExpression call, params IBoundExpression[] operands)
        {
            var leaves = new List<BoundFeature>();
            foreach (var operand in operands)
            {
                BoundTree.CollectFeatures(operand, leaves);
            }

            // the bar timeframes in the order they are written, then the date; a null stands for the date
            var clocks = leaves.Select(l => IsDateDriven(l) ? (Timeframe?)null : l.Spec.Timeframe).Distinct().OrderBy(c => c is null).ToList();
            if (clocks.Count < 2)
            {
                return;
            }

            var names = clocks.Select(c => c is { } frame ? frame.ToCode() : DateClock).ToList();
            var explicitForm = call is BoundCrossing crossing ? ExplicitCrossing(crossing) : null;
            scope.Error(
                LintCodes.MixedTimeframesInLag,
                scope.Quoted(call.Span) + " mixes the timeframes " + string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
                    + ": the previous value of each side would be a different moment. Compare on one timeframe"
                    + (explicitForm is null ? "." : ", or say what you mean: `" + explicitForm + "`."),
                call.Span,
                explicitForm);
        }

        /// <summary>A calendar or context feature: it changes once a date, so its previous value is the previous date, whatever the timeframe it is read on.</summary>
        private static bool IsDateDriven(BoundFeature feature) => IndicatorCatalog.TryGet(feature.Spec.Name, out var info) && info.DateDriven;

        /// <summary>
        /// The same crossing written out with one side lagged by hand, when one side is a plain reference on the strategy's own timeframe: <c>close &gt; tf("1D").ema(20) and close[1] &lt;= tf("1D").ema(20)</c>. Null
        /// when no side qualifies (a call cannot be given a subscript).
        /// </summary>
        private string? ExplicitCrossing(BoundCrossing crossing)
        {
            var a = Operand(crossing.Left);
            var b = Operand(crossing.Right);
            var (above, below) = crossing.IsAbove ? (">", "<=") : ("<", ">=");
            if (CanLag(crossing.Left))
            {
                return a + " " + above + " " + b + " and " + a + "[1] " + below + " " + b;
            }

            return CanLag(crossing.Right) ? a + " " + above + " " + b + " and " + a + " " + below + " " + b + "[1]" : null;
        }

        /// <summary>
        /// A plain reference (not a call, not in parentheses, not subscripted) on the strategy's own timeframe: the one kind of operand that can be given a subscript by hand. Never a feature of the date: its
        /// subscript would be the previous date, the very mix the explicit form is there to avoid.
        /// </summary>
        private bool CanLag(IBoundExpression operand) =>
            operand is BoundFeature { IsReference: true, Lag: 0 } feature && !IsDateDriven(feature) && feature.Spec.Timeframe == scope.EvaluationTimeframe && !scope.Text(operand.Span).StartsWith('(');

        private string Operand(IBoundExpression operand)
        {
            var text = scope.Text(operand.Span);
            return operand is BoundFeature or BoundNumber ? text : "(" + text + ")";
        }
    }
}
