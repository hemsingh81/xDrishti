namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>What the comparison of one series found: how many candidates and trades, the oracle's trace, and every mismatch with where it was seen.</summary>
internal sealed record SeriesResult(string Generator, long Seed, int Category, string Mode, int Candidates, int Trades, int Blocked, bool Gated, IReadOnlySet<string> Trace, IReadOnlyList<(string Where, Mismatch Mismatch)> Mismatches)
{
    internal bool Agrees => Mismatches.Count == 0;
}

/// <summary>
/// The comparison of one series (spec P3-08 D3 to D5): the oracle walks (and gates) the candidates, the engine does the same through <see cref="OracleAdapter"/>, and every field of D4 is compared under D5. The oracle is always run with
/// <see cref="OracleFaults.None"/>: a production comparison never switches a fault on (the architecture test scans the IL for it).
/// </summary>
internal static class SeriesComparison
{
    internal static SeriesResult Run(ComparisonCase series, RatioStats stats)
    {
        var mismatches = new List<(string, Mismatch)>();
        var trace = new HashSet<string>(StringComparer.Ordinal);
        int candidates = 0, trades = 0, blocked = 0;
        var gated = false;
        var groupNumber = 0;
        foreach (var group in series.Groups)
        {
            var oracleRun = group.Oracle.Run(OracleFaults.None);
            trace.UnionWith(oracleRun.Rows);
            IReadOnlyList<OracleResult> engine;
            IReadOnlyList<bool> engineBlocked;
            if (group.EngineResults is { } given)
            {
                engine = [.. given.Select(OracleAdapter.Result)];
                engineBlocked = [.. given.Select(o => o.NoTrade == XDrishti.Domain.Research.NoTradeReason.PositionOpen)];
            }
            else
            {
                var walked = OracleAdapter.Walk(series.Bars, group.EngineCandidates, group.EngineRules, group.Exit);
                engine = walked.Results;
                engineBlocked = walked.Blocked;
                foreach (var echo in walked.Echoes)
                {
                    mismatches.Add(($"group {groupNumber}", echo));
                }
            }

            if (engine.Count != oracleRun.Results.Count)
            {
                mismatches.Add(($"group {groupNumber}", new Mismatch("results", oracleRun.Results.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), engine.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                groupNumber++;
                continue;
            }

            gated |= group.Oracle.Rules.Hold is not null && oracleRun.Results.Count > 1;
            for (var i = 0; i < engine.Count; i++)
            {
                var where = $"group {groupNumber} candidate {i}";
                foreach (var mismatch in OracleComparer.Diff(oracleRun.Results[i], engine[i], stats, $"{series.Generator} {series.Seed} {series.Mode} {where}"))
                {
                    mismatches.Add((where, mismatch));
                }

                candidates++;
                trades += oracleRun.Results[i].Trade is null ? 0 : 1;
                blocked += oracleRun.Results[i].NoTrade == OracleNoTradeReason.PositionOpen ? 1 : 0;
                if ((oracleRun.Results[i].NoTrade == OracleNoTradeReason.PositionOpen) != engineBlocked[i])
                {
                    mismatches.Add((where, new Mismatch("Blocked", "oracle gate", "engine gate")));
                }
            }

            groupNumber++;
        }

        return new SeriesResult(series.Generator, series.Seed, series.Category, series.Mode, candidates, trades, blocked, gated, trace, mismatches);
    }
}
