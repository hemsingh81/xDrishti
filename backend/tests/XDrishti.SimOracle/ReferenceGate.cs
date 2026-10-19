namespace XDrishti.SimOracle;

/// <summary>The position gate of ADR 0040 D11 with the hold end of ADR 0047 R13: one instrument, one side, candidates in decision order.</summary>
public static class ReferenceGate
{
    /// <summary>
    /// Runs the candidates in order. A candidate with decision instant D is blocked when D &lt;= E (the hold end of the last accepted trade)
    /// or when the last accepted trade is an EndOfData trade; a blocked candidate is not walked.
    /// </summary>
    public static OracleGateResult Run(IReadOnlyList<OracleCandidate> candidates, Func<OracleCandidate, OracleResult> walk)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(walk);
        var entries = new List<OracleGateEntry>();
        var rows = new HashSet<string>(StringComparer.Ordinal);
        var total = new OracleCounters();
        OracleTrade? last = null;
        long lastEnd = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            OracleCandidate c = candidates[i];
            long d = c.DecisionEpochSeconds;
            bool endOfDataBlock = last is not null && last.ExitReason == OracleExitReason.EndOfData;
            bool blocked = last is not null && (endOfDataBlock || d <= lastEnd);
            OracleResult result;
            if (blocked)
            {
                rows.Add(OracleRows.D11);
                if (endOfDataBlock)
                {
                    rows.Add(OracleRows.D11EndOfData);
                }

                result = new OracleResult(
                    null,
                    OracleNoTradeReason.PositionOpen,
                    new OracleCounters { Candidates = 1, PositionOpen = 1 },
                    new HashSet<string>(StringComparer.Ordinal) { OracleRows.D11 },
                    null);
            }
            else
            {
                if (last is not null
                    && OracleClock.DateOf(last.ExitEpochSeconds) == OracleClock.DateOf(d - 60)
                    && last.ExitReason == OracleExitReason.MaxHold
                    && lastEnd == last.ExitEpochSeconds
                    && d > lastEnd)
                {
                    rows.Add(OracleRows.D11Close);
                }

                result = walk(c);
                rows.UnionWith(result.Rows);
                if (result.Trade is not null)
                {
                    last = result.Trade;
                    lastEnd = result.HoldEndEpochSeconds ?? result.Trade.ExitEpochSeconds;
                }
            }

            entries.Add(new OracleGateEntry(c, blocked, result, result.HoldEndEpochSeconds));
            total = total.Plus(result.Counters);
        }

        return new OracleGateResult(entries, total, rows);
    }
}
