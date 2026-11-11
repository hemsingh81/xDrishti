using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Learning;

/// <summary>
/// Computes and keeps the cell statistics (spec P6-01, ADR 0064): validates the command without the database, reads the closed trades of each named stored run, groups them into cells (strategy, timeframe, side from the trades; style and exit from the
/// source), runs the pure <see cref="CellStatsCalculator"/> and appends the snapshot. The same inputs give the same snapshot key and a second run writes nothing. R is net of charges when the run was costed, else from the filled prices; each row says which.
/// Nothing of a trade is logged.
/// </summary>
internal sealed class ComputeCellStatsHandler(IRunStore runs, ICellStatsStore store, IContextFigures contextFigures, TimeProvider time, ILogger<ComputeCellStatsHandler> logger, ConfidenceOptions? confidence = null, ShrinkageOptions? shrinkage = null, IUniverseSeriesSource? universeSource = null) : ICommandHandler<ComputeCellStatsCommand, CellStatsOutcome>
{
    /// <summary>The most runs one snapshot reads.</summary>
    public const int MaxSources = 50;

    private static readonly string[] Styles = ["intraday", "swing"];

    private static readonly UniverseTierOptions TierOptions = new();

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
        var read = await CellSourceReader.ReadAsync(runs, command.Sources, cancellationToken);
        if (read.IsFailure)
        {
            CellStatsLog.Refused(logger, read.Error.Code);
            return Result.Failure<CellStatsOutcome>(read.Error);
        }

        var ordered = read.Value.Cells;
        var tradeCount = read.Value.Trades.Count;
        RegimeCells? regime = null;
        string? regimeText = null;
        if (command.ByRegime)
        {
            // P6-02 (ADR 0146): the label of each trade is the regime of the day before its entry day, or the reason there is none; the labels used are part of the snapshot key.
            var labels = await RegimeTags.LabelsAsync(contextFigures, read.Value.Trades.Select(t => t.Trade.TradeDate), cancellationToken);
            var byCell = read.Value.Trades.ToLookup(t => t.CellKey, StringComparer.Ordinal);   // in the order the trades were read, which is the order of CellInput.Trades
            var perCell = ordered.Select(c => (IReadOnlyList<RegimeLabel>)[.. byCell[c.Cell.Key].Select(t => labels[t.Trade.TradeDate])]).ToList();
            regime = RegimeCellBuilder.Build(ordered, perCell);
            regimeText = RegimeTags.Canonical(labels);
        }

        UniverseCells? universe = null;
        string? universeText = null;
        if (command.ByUniverse)
        {
            if (universeSource is null)
            {
                CellStatsLog.Refused(logger, LearningErrors.InvalidCode);
                return Result.Failure<CellStatsOutcome>(LearningErrors.Invalid([new FieldError("byUniverse", "The tier source is not available.")]));
            }

            // BK-11 (ADR 0261): the tier of a trade is the tier of its instrument on its decision date, from daily bars before that date only; the tiers used are part of the snapshot key.
            var tagged = await UniverseTagger.TagAsync(universeSource, [.. read.Value.Trades.Select(t => (t.Trade.TradeDate, t.Trade.InstrumentId))], TierOptions, cancellationToken);
            var perKey = new Dictionary<string, List<UniverseTier>>(StringComparer.Ordinal);   // in the order the trades were read, which is the order of CellInput.Trades
            for (var i = 0; i < tagged.Count; i++)
            {
                var key = read.Value.Trades[i].CellKey;
                if (!perKey.TryGetValue(key, out var list))
                {
                    perKey[key] = list = [];
                }

                list.Add(tagged[i]);
            }

            universe = UniverseCellBuilder.Build(ordered, [.. ordered.Select(c => (IReadOnlyList<UniverseTier>)perKey[c.Cell.Key])]);
            universeText = UniverseTagger.Canonical(TierOptions, tagged);
        }

        var rows = CellStatsCalculator.Compute(ordered, command.AsOf, command.Options, regime?.Cells, confidence, shrinkage, universe?.Cells);
        var sources = string.Join(',', command.Sources.OrderBy(s => s.RunKey, StringComparer.Ordinal).Select(s => $"{s.RunKey}:{s.Style}:{s.Exit}"));
        var key64 = KeyOf(command.AsOf, command.Options, sources, regimeText, confidence, shrinkage, universeText);
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

        var shrunk = rows.Count(r => r.Shrinkage is { IsAvailable: true });
        var floorRows = rows.Count(r => r.Shrinkage is { IsAvailable: true, TauSource: "floor" });
        var universeTally = universe is null ? null : new UniverseTally(universe.Cells.Count, universe.Cells.Sum(c => c.Trades.Count), universe.Unknown, universe.Unavailable);
        var outcome = new CellStatsOutcome(key64, ordered.Count, rows.Count, tradeCount, !written, regime?.Tally, regime?.Cells.Count ?? 0, shrunk, floorRows, universeTally);
        var asOfText = command.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var elapsedMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        CellStatsLog.Completed(logger, key64[..12], asOfText, outcome.Cells, outcome.Rows, tradeCount, written ? "stored" : "already stored", elapsedMs);
        if (universeTally is not null)
        {
            CellStatsLog.Universe(logger, key64[..12], universeTally.TierCells, universeTally.Tiered, universeTally.Unknown, universeTally.Unavailable);
        }

        if (shrinkage is { IsOn: true })
        {
            CellStatsLog.Shrinkage(logger, key64[..12], shrunk, floorRows);
        }

        return Result.Success(outcome);
    }

    internal static string KeyOf(DateOnly asOf, CellStatsOptions o, string sources, string? regimeLabels = null, ConfidenceOptions? confidence = null, ShrinkageOptions? shrinkage = null, string? universeTiers = null)
    {
        // the regime part is appended only when the split was asked for, so the key of every snapshot without it is the key it always had; the same for the confidence part (CF-01), which is added only when effective n is computed, and the shrinkage part (CF-02, with the sigma floor of CF-01 it reads), added only when shrinkage is computed, so a changed setting is a new snapshot; the universe part (BK-11) is added only when the tier split was asked for
        var text = string.Create(CultureInfo.InvariantCulture, $"cell-stats/1|{asOf:yyyy-MM-dd}|{o.RecencyHalfLifeDays:R}|{o.PriorStrength:R}|{o.MinTrades}|{o.CredibleLevel:R}|{sources}") + (regimeLabels is null ? string.Empty : "|regime=" + regimeLabels)
            + (confidence is { IsOn: true } ? "|confidence=" + confidence.KeyText() : string.Empty)
            + (shrinkage is { IsOn: true } ? "|shrinkage=" + shrinkage.KeyText() + "/" + (confidence ?? new ConfidenceOptions()).SigmaFloorR.ToString("R", CultureInfo.InvariantCulture) : string.Empty)
            + (universeTiers is null ? string.Empty : "|universe=" + universeTiers);
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
