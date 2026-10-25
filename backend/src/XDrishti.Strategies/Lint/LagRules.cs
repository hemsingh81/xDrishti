using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Binding;

namespace XDrishti.Strategies.Lint;

/// <summary>
/// The lag rules (spec P3-02, <i>Meaning</i> and <i>Lint rules</i>). <b>History.</b> A feature is read at its own lag (<c>close[2]</c>) and, inside <c>crosses_*</c> (lags 0 and 1) and <c>rising/falling</c> (lags 0 to n),
/// at that many more closed bars back: the lags add up, so <c>rising(close[200], 100)</c> reads 300 bars back. The frame keeps <see cref="FrameOptions.MaxHistoryDepth"/> values (lags 0 to 255), so a condition whose
/// largest lag plus 1 is above it is <c>lag_too_deep</c>; the largest lag plus 1 is the history depth the plan asks for. <b>Clocks.</b> A lagged evaluation reads "the previous value" of each feature on its own
/// clock; if the features of one <c>crosses_*</c>, <c>rising</c> or <c>falling</c> run on different clocks the previous values would be different moments, so it is <c>mixed_timeframes_in_lag</c>. A clock is a bar
/// timeframe (<c>close</c> on 15m: the previous 15-minute bar) or <b>the date</b>: the calendar and context features (<see cref="IndicatorInfo.DateDriven"/>) are read on 1m whatever the strategy runs on, but change
/// once a date and their previous value is the previous <i>date</i>, never the previous minute. So they are a clock of their own on every evaluation timeframe, 1m included: they may be lagged among themselves and
/// against constants, never against a bar feature (ADR 0034, Amendment 4). <b>Sessions.</b> The twelve features that start again at each India date (<see cref="IndicatorInfo.RestartsEachSession"/>) keep the history of
/// the current session only in the frame (ADR 0035), so a lagged read of one of them never compares two sessions: on the first bar of a session <c>vwap[1]</c> is unknown, and a crossing against it is unknown, not a cross of
/// the last bar of yesterday with the first of today. That is a rule of the frame and needs no help from the condition. What is still worth a word is a read that can <i>never</i> be known: <c>k</c> bars of timeframe <c>T</c> back
/// when a whole session holds no more than <c>k</c> of them (<c>k · T</c> is at least the 375 minutes of a session), because every bar that far back is in an earlier session, where the feature has no value. That is the warning
/// <c>lag_across_session</c>, whatever the strategy's own timeframe is; a read of a restarting feature on the 1D lane is by position and is not warned (ADR 0035).
/// </summary>
internal static class LagRules
{
    /// <summary>The history depth the plan needs: the largest lag read plus 1, at least 1 (only meaningful when no error was found).</summary>
    public static int Apply(IBoundExpression root, BindingScope scope)
    {
        var walk = new Walk(scope);
        walk.Visit(root, 0, null);
        walk.ReportSessionReads();
        return (int)Math.Min(Math.Max(walk.Deepest + 1, 1), int.MaxValue);
    }

    /// <summary>What one call (or lone subscripted feature) reads of restarting features from further back than a session holds: which, how far back, and how many bars of their timeframe a session holds.</summary>
    private sealed class SessionRead(IBoundExpression owner, Timeframe timeframe)
    {
        public IBoundExpression Owner { get; } = owner;

        /// <summary>The timeframe of the first feature recorded (the features of one call share a clock, or the call is already an error).</summary>
        public Timeframe Timeframe { get; } = timeframe;

        public List<string> Names { get; } = [];

        public long Deepest { get; set; }
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
        /// Remembers a read <paramref name="lag"/> closed bars back of a feature that restarts each session (<see cref="IndicatorInfo.RestartsEachSession"/>) when it reaches back further than a whole session holds:
        /// <c>lag · T</c> minutes, with <c>T</c> the minutes of the feature's own timeframe, is at least the 375 minutes of a session, so every bar that far back is in an earlier session and the frame has no value for it.
        /// The strategy's own timeframe does not matter: the read is of the feature's lane.
        /// </summary>
        private void NoteSessionRead(BoundFeature feature, long lag, IBoundExpression owner)
        {
            if (lag < 1 || !IndicatorCatalog.TryGet(feature.Spec.Name, out var info) || !info.RestartsEachSession(feature.Spec) || lag * feature.Spec.Timeframe.Minutes() < SessionClock.RegularBars)
            {
                return;
            }

            if (!_sessionReadOf.TryGetValue(owner, out var read))
            {
                read = new SessionRead(owner, feature.Spec.Timeframe);
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
        }

        /// <summary>Warns about every call (or lone subscripted feature) that reads a restarting feature from further back than a session holds: that part of the condition is never known.</summary>
        public void ReportSessionReads()
        {
            foreach (var read in _sessionReads)
            {
                var minutes = read.Timeframe.Minutes();
                var barsInSession = (SessionClock.RegularBars + minutes - 1) / minutes;
                var names = string.Join(", ", read.Names.Select(BindingScope.Quote));
                scope.Warning(
                    LintCodes.LagAcrossSession,
                    scope.Quoted(read.Owner.Span) + " reads " + names + " up to " + read.Deepest.ToString(CultureInfo.InvariantCulture) + " closed bars back, but a session holds at most " + barsInSession.ToString(CultureInfo.InvariantCulture) + " bars of " + read.Timeframe.ToCode()
                        + " and a per-session feature has no value from an earlier session: that part of the condition is never known, and unknown is not true. Read at most "
                        + (barsInSession - 1).ToString(CultureInfo.InvariantCulture) + " bars back.",
                    read.Owner.Span);
            }
        }

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
                    + ": the previous value of each side would be a different moment. " + Advice(explicitForm, clocks.Contains(null)),
                call.Span,
                explicitForm);
        }

        /// <summary>
        /// What to do instead. With an explicit form, it is offered as the second way out. Without one, "compare on one timeframe" cannot be followed when the date is one of the clocks (a feature of the date has no other
        /// timeframe: it changes once a date), so the advice names the date.
        /// </summary>
        private static string Advice(string? explicitForm, bool dateIsAClock) =>
            explicitForm is not null ? "Compare on one timeframe, or say what you mean: `" + explicitForm + "`."
            : dateIsAClock ? "A calendar or context feature changes once a date, so compare it without a lag, or lag only the bar side by hand."
            : "Compare on one timeframe.";

        /// <summary>A calendar or context feature: it changes once a date, so its previous value is the previous date, whatever the timeframe it is read on.</summary>
        private static bool IsDateDriven(BoundFeature feature) => IndicatorCatalog.TryGet(feature.Spec.Name, out var info) && info.DateDriven;

        /// <summary>
        /// The same crossing written out with one side lagged by hand, when one side is a plain reference on the strategy's own timeframe: <c>close &gt; tf("1D").ema(20) and close[1] &lt;= tf("1D").ema(20)</c>. Null
        /// when no side qualifies (only a plain reference is lagged by hand here).
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
