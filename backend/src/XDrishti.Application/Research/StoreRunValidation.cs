using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>The checks of a run before the database is touched (spec P3-10, D8). Every problem is listed, at most <see cref="MaxProblems"/>.</summary>
internal static class StoreRunValidation
{
    /// <summary>The most problems reported.</summary>
    public const int MaxProblems = 20;

    public static IReadOnlyList<FieldError> Check(SimulationRun? run, RunRecordText? record, string? actor)
    {
        var problems = new List<FieldError>();
        if (run is null)
        {
            problems.Add(new FieldError("run", "The run is missing."));
            return problems;
        }

        if (record is null)
        {
            problems.Add(new FieldError("record", "The record text is missing."));
        }
        else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record.Json))), record.RecordSha256, StringComparison.Ordinal))
        {
            problems.Add(new FieldError("record.recordSha256", "The SHA-256 of the record text is not the digest given."));
        }

        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (!RunRecordText.IsHex64(run.RunKey))
        {
            problems.Add(new FieldError("run.runKey", "The run key is 64 lower-case hex characters."));
        }

        if (run.SchemaVersion != SimulationRun.CurrentSchemaVersion)
        {
            problems.Add(new FieldError("run.schemaVersion", $"Record schema {run.SchemaVersion} cannot be stored; this store holds schema {SimulationRun.CurrentSchemaVersion}."));
        }

        CheckColumns(run, record, problems);
        CheckTrades(run, problems);
        CheckCosts(run, problems);
        return problems.Count > MaxProblems ? problems.GetRange(0, MaxProblems) : problems;
    }

    /// <summary>Lengths and formats the columns hold (a char(64) would pad a shorter value with spaces; a longer text would fail in the database): refused here as a validation, not as a write failure.</summary>
    private static void CheckColumns(SimulationRun run, RunRecordText? record, List<FieldError> problems)
    {
        void Length(string field, string? value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length > max)
            {
                problems.Add(new FieldError(field, $"Must be 1 to {max} characters."));
            }
        }

        void Hex(string field, string? value)
        {
            if (!RunRecordText.IsHex64(value))
            {
                problems.Add(new FieldError(field, "Must be 64 lower-case hex characters."));
            }
        }

        if (run.Strategy is null || run.Universe is null)
        {
            problems.Add(new FieldError("run.strategy", "The strategy and the universe are required."));
            return;
        }

        Length("run.strategy.id", run.Strategy.Id, 64);
        Hex("run.strategy.definitionSha256", run.Strategy.DefinitionSha256);
        Length("run.universe.label", run.Universe.Label, 200);
        Length("run.fills", run.Fills, 2000);
        Length("run.engineVersion", run.EngineVersion, 32);
        if (run.Costs is { } costs)
        {
            Length("run.costs.sheetId", costs.SheetId, 64);
            Hex("run.costs.ratesSha256", costs.RatesSha256);
        }

        if (record is not null)
        {
            Hex("record.tradesSha256", record.TradesSha256);
        }

        if (run.Trades is null || run.OpenAtEnd is null)
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trade in run.Trades.Concat(run.OpenAtEnd))
        {
            var before = problems.Count;
            Length("run.trades.symbol", trade.Trade.Symbol, 40);
            Length("run.trades.strategyId", trade.Trade.StrategyId, 64);
            Length("run.trades.key", trade.Trade.Key, 160);
            for (var i = problems.Count - 1; i >= before; i--)
            {
                if (!reported.Add(problems[i].Field))
                {
                    problems.RemoveAt(i);
                }
            }
        }
    }

    private static void CheckTrades(SimulationRun run, List<FieldError> problems)
    {
        if (run.Trades is null || run.OpenAtEnd is null)
        {
            problems.Add(new FieldError("run.trades", "The trade lists are missing."));
            return;
        }

        // the record's own count of the trades dated in the window includes the open-at-end ones; the lists cannot hold more than it counted
        var held = (long)run.Trades.Count + run.OpenAtEnd.Count;
        if (run.Totals is { } totals && totals.TradesInWindow < held)
        {
            problems.Add(new FieldError("run.trades", $"The record counts {totals.TradesInWindow} trades in the window but the lists hold {held}."));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var wrongSchema = 0;
        var withLegs = 0;
        var duplicateReported = false;
        foreach (var trade in run.Trades.Concat(run.OpenAtEnd))
        {
            if (trade.Trade.SchemaVersion != SimTrade.CurrentSchemaVersion)
            {
                wrongSchema++;
            }

            if (trade.Trade.Legs is not null)
            {
                withLegs++;
            }

            if (!keys.Add(trade.Trade.Key) && !duplicateReported)
            {
                duplicateReported = true;
                problems.Add(new FieldError("run.trades.key", $"Two trades have the key '{trade.Trade.Key}'."));
            }
        }

        if (withLegs > 0)
        {
            // Shortcut (P4-01 D15, kept by P4-02): the legs are not persisted. `simulate --store` of a PARTIAL_BE or PARTIAL_TRAIL run is refused here by name and the export files keep the legs; the columns
            // (`legs jsonb`, `cost_leg_charges jsonb`, migration AddSimTradeLegs) come with the first slice that must read a stored trade with legs back (P4-05, the comparison), as ADR 0050 decides.
            problems.Add(new FieldError("run.trades.legs", $"{withLegs} trades have legs; the store has no column for them yet (P4-01, D15): the legs stay in the run folder."));
        }

        if (wrongSchema > 0)
        {
            problems.Add(new FieldError("run.trades.schemaVersion", $"{wrongSchema} trades are not of schema {SimTrade.CurrentSchemaVersion}."));
        }
    }

    private static void CheckCosts(SimulationRun run, List<FieldError> problems)
    {
        if (run.Costs is { } costs && costs.SheetId.StartsWith(CostSheet.TestPrefix, StringComparison.Ordinal))
        {
            problems.Add(new FieldError("run.costs.sheetId", $"A run costed by a '{CostSheet.TestPrefix}' sheet is synthetic and is never stored."));
        }

        if (run.Trades is null || run.OpenAtEnd is null)
        {
            return;
        }

        var all = run.Trades.Concat(run.OpenAtEnd);
        if (run.Costs is null)
        {
            if (all.Any(t => t.Trade.Costing is not null || t.Trade.Costs == CostBasis.Charged))
            {
                problems.Add(new FieldError("run.trades.costing", "A gross run has a costed trade."));
            }

            return;
        }

        if (all.Any(t => t.Trade.Costing is null || t.Trade.Costs != CostBasis.Charged))
        {
            problems.Add(new FieldError("run.trades.costing", "A costed run has a gross trade."));
        }

        if (all.Any(t => t.Trade.Costing is { } costing && !string.Equals(costing.SheetId, run.Costs.SheetId, StringComparison.Ordinal)))
        {
            problems.Add(new FieldError("run.trades.costing.sheetId", "A trade is costed with another sheet than the run's."));
        }
    }
}
