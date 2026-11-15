using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sim;

/// <summary>What settling one day gave: the SR-01 session record, its canonical text and hash, the trades of the day and the closing equity.</summary>
internal sealed record SimDayOutcome(ReplaySessionRecord Record, string RecordJson, string ResultHash, string TradesJson, decimal EquityClose);

/// <summary>
/// The in-memory book of one simulation (SR-04, ADR 0293): a stored session cursor over an SR-01 <see cref="ReplayPrepared"/>. A day is <see cref="ReplayDriver.StepAsync"/> of that session (no second engine, no copy of the selection), so a day equals the SR-01 session with the same inputs.
/// Each day is stepped with the end that was in force when it was settled, so a run that Continue extended rebuilds its old days byte for byte (the last old day kept its "end of range": nothing before the old end is recomputed).
/// </summary>
internal sealed class SimDayBook(ReplayDriver driver, ReplayPrepared prepared)
{
    /// <summary>The last day stepped; null before the first.</summary>
    public DateOnly? Through { get; private set; }

    /// <summary>The end the book was prepared for.</summary>
    public DateOnly End => prepared.Run.Parameters.To;

    /// <summary>The trading sessions from the start to the end.</summary>
    public IReadOnlyList<DateOnly> Sessions => prepared.Sessions;

    /// <summary>Prepares the book of a stored run: its stored parameters with the end in force, settings snapshot, strategy versions and learning sources, and the widened clamp when it runs inside hold-out block 1.</summary>
    public static async ValueTask<Result<SimDayBook>> OpenAsync(ReplayDriver driver, IReplayFacts facts, SimDaysRunRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var parameters = ReplayJson.Read<ReplayParams>(row.ParamsJson);
        var settings = ReplayJson.Read<ReplaySettingsSnapshot>(row.SettingsJson);
        var stored = ReplayJson.Read<Dictionary<string, List<StoredVersion>>>(row.VersionsJson);
        if (parameters is null || settings is null || stored is null)
        {
            return Result.Failure<SimDayBook>(Error.Validation("sim.unreadable", "The stored parameters, settings snapshot or strategy versions cannot be read."));
        }

        if (row.ReadThrough is { } through && !facts.ReadInsideHoldOut(through))
        {
            return Result.Failure<SimDayBook>(Error.Conflict("sim.holdout", $"The run reads inside the hold-out block up to {through:yyyy-MM-dd}, but the replay cannot read there: the day cannot be settled."));
        }

        var versions = stored.ToDictionary(p => p.Key, p => (IReadOnlyList<StoredVersion>)p.Value, StringComparer.Ordinal);
        var p = parameters with { To = row.End };
        var ready = await driver.PrepareAsync(p, settings, versions, cancellationToken, row.LearningSources, p.Mode == ReplayMode.Learning);
        return ready.IsFailure ? Result.Failure<SimDayBook>(ready.Error) : Result.Success(new SimDayBook(driver, ready.Value));
    }

    /// <summary>Settles <paramref name="day"/> (the next session after <see cref="Through"/>) with <paramref name="endInForce"/> as the last session of the range.</summary>
    public async ValueTask<Result<SimDayOutcome>> StepAsync(DateOnly day, DateOnly endInForce, CancellationToken cancellationToken)
    {
        var view = new ReplayPrepared(prepared.Run with { LastAllowed = endInForce }, prepared.Book, prepared.Sessions);
        var stepped = await driver.StepAsync(view, day, cancellationToken);
        if (stepped.IsFailure)
        {
            return Result.Failure<SimDayOutcome>(stepped.Error);
        }

        Through = day;
        var record = stepped.Value;
        var text = ReplayJson.Canonical(record);
        var trades = prepared.Book.Trades
            .Where(t => t.EntrySession == day || t.ExitSession == day)
            .Select(t =>
            {
                var exit = t.Exit is { } e && t.ExitSession <= day ? e : null;
                return new SimDayTrade(t.Id, t.Item.Symbol, t.Item.Side, t.Item.Evidence.CellKey, t.Qty, t.EntrySession, t.EntryAt, t.EntryPrice, t.StopPrice, t.Swing, exit is null ? null : t.ExitSession, exit?.ExitAt, exit?.GrossPnl, exit?.RSim, t.InstrumentId, exit?.ExitPrice, exit?.ExitReason);
            })
            .ToList();
        return Result.Success(new SimDayOutcome(record, text, ReplayJson.Sha256(text), ReplayJson.Canonical(trades), record.Equity));
    }

    /// <summary>The ledger figures of the book at the close of <paramref name="day"/> (only <see cref="Domain.Paper.PaperLedger"/>).</summary>
    public Domain.Paper.LedgerFigures FiguresAsOf(DateOnly day) => prepared.Book.FiguresAsOf(day);

    /// <summary>Every trade of the book so far (for the reconciliation).</summary>
    public IReadOnlyList<ReplayTrade> Trades => prepared.Book.Trades;
}
