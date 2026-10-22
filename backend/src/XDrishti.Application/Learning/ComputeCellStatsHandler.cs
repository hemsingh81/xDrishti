using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>
/// Computes and keeps the cell statistics (spec P6-01, ADR 0064): validates the command without the database, reads the closed trades of each named stored run, groups them into cells (strategy, timeframe, side from the trades; style and exit from the
/// source), runs the pure <see cref="CellStatsCalculator"/> and appends the snapshot. The same inputs give the same snapshot key and a second run writes nothing. R is net of charges when the run was costed, else from the filled prices; each row says which.
/// Nothing of a trade is logged.
/// </summary>
internal sealed class ComputeCellStatsHandler(IRunStore runs, ICellStatsStore store, TimeProvider time, ILogger<ComputeCellStatsHandler> logger) : ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>
{
    /// <summary>The most runs one snapshot reads.</summary>
    public const int MaxSources = 50;

    private static readonly string[] Styles = ["intraday", "swing"];

    public async ValueTask<Result<CellStatsOutcome>> HandleAsync(ComputeCellStatsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Check(command);
        if (problems.Count > 0)
        {
            CellStatsLog.Refused(logger, LearningErrors.InvalidCode);
            return Result.Failure<CellStatsOutcome>(LearningErrors.Invalid(problems));
        }

        var started = time.GetTimestamp();
        var inputs = new Dictionary<string, (CellId Cell, bool Costed, List<CellTrade> Trades)>(StringComparer.Ordinal);
        var tradeCount = 0;
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var strategyOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in command.Sources)
        {
            if (await runs.FindAsync(source.RunKey, cancellationToken) is null)
            {
                CellStatsLog.Refused(logger, LearningErrors.RunNotFoundCode);
                return Result.Failure<CellStatsOutcome>(LearningErrors.RunNotFound(source.RunKey));
            }

            await foreach (var runTrade in runs.StreamTradesAsync(source.RunKey, false, cancellationToken))
            {
                var t = runTrade.Trade;
                var id = new CellId(t.StrategyId, t.TriggerTimeframe.ToCode(), t.Side.ToString().ToUpperInvariant(), source.Style, source.Exit);
                var costed = t.Costs == CostBasis.Charged && t.Costing is not null;
                var r = costed ? t.Costing!.RNet : t.RFilled;
                if (!inputs.TryGetValue(id.Key, out var cell))
                {
                    cell = (id, costed, []);
                    inputs[id.Key] = cell;
                }

                if (!strategyOwner.TryAdd(t.StrategyId, source.RunKey) && !string.Equals(strategyOwner[t.StrategyId], source.RunKey, StringComparison.Ordinal))
                {
                    CellStatsLog.Refused(logger, LearningErrors.CellDuplicateCode);
                    return Result.Failure<CellStatsOutcome>(LearningErrors.StrategyDuplicate(t.StrategyId));
                }

                if (cell.Costed != costed)
                {
                    CellStatsLog.Refused(logger, LearningErrors.MixedBasisCode);
                    return Result.Failure<CellStatsOutcome>(LearningErrors.MixedBasis(id.Key));
                }

                if (!owner.TryAdd(id.Key, source.RunKey) && !string.Equals(owner[id.Key], source.RunKey, StringComparison.Ordinal))
                {
                    CellStatsLog.Refused(logger, LearningErrors.CellDuplicateCode);
                    return Result.Failure<CellStatsOutcome>(LearningErrors.CellDuplicate(id.Key));
                }

                cell.Trades.Add(new CellTrade(SessionClock.DateOf(t.ExitEpochSeconds), r, t.MaeR, t.MfeR));
                tradeCount++;
            }
        }

        var ordered = inputs.Values.OrderBy(c => c.Cell.Key, StringComparer.Ordinal).Select(c => new CellInput(c.Cell, c.Costed, c.Trades)).ToList();
        var rows = CellStatsCalculator.Compute(ordered, command.AsOf, command.Options);
        var sources = string.Join(',', command.Sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}"));
        var key64 = KeyOf(command.AsOf, command.Options, sources);
        var snapshot = new CellSnapshotToStore(key64, command.AsOf, command.Options, sources, rows, command.Actor, time.GetUtcNow());
        bool written;
        try
        {
            written = await store.StoreAsync(snapshot, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            CellStatsLog.Failed(logger, key64[..12]);
            throw;
        }

        var outcome = new CellStatsOutcome(key64, ordered.Count, rows.Count, tradeCount, !written);
        var asOfText = command.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        CellStatsLog.Completed(logger, key64[..12], asOfText, outcome.Cells, outcome.Rows, tradeCount, written ? "stored" : "already stored", elapsedMs);
        return Result.Success(outcome);
    }

    internal static string KeyOf(DateOnly asOf, CellStatsOptions o, string sources)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"cell-stats/1|{asOf:yyyy-MM-dd}|{o.RecencyHalfLifeDays:R}|{o.PriorStrength:R}|{o.MinTrades}|{o.CredibleLevel:R}|{sources}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private List<FieldError> Check(ComputeCellStatsCommand command)
    {
        var problems = new List<FieldError>();
        if (command.Options is null)
        {
            problems.Add(new FieldError("options", "The options are missing."));
            return problems;
        }

        foreach (var p in command.Options.Problems())
        {
            problems.Add(new FieldError("options", p));
        }

        if (command.Sources is null || command.Sources.Count is < 1 or > MaxSources)
        {
            problems.Add(new FieldError("sources", $"Name 1 to {MaxSources} runs."));
            return problems;
        }

        var today = SessionClock.DateOf(time.GetUtcNow().ToUnixTimeSeconds());
        if (command.AsOf == default || command.AsOf > today)
        {
            problems.Add(new FieldError("asOf", "The as-of date is required and not in the future."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        foreach (var s in command.Sources)
        {
            if (!RunRecordText.IsHex64(s.RunKey))
            {
                problems.Add(new FieldError("sources.runKey", "A run key is 64 lower-case hex characters."));
            }

            if (!Styles.Contains(s.Style, StringComparer.Ordinal))
            {
                problems.Add(new FieldError("sources.style", "The style is intraday or swing."));
            }

            if (new CellId("x", "x", "x", "x", s.Exit ?? string.Empty).Problems().Count > 0)
            {
                problems.Add(new FieldError("sources.exit", "The exit label is required, at most 64 characters and without '|'."));
            }
        }

        if (command.Sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal).Count() != command.Sources.Count)
        {
            problems.Add(new FieldError("sources", "A run is named twice."));
        }

        return problems;
    }
}
