namespace XDrishti.Application.Abstractions.OptionPhaseCheck;

/// <summary>The header of a stored expression run as the phase check reads it: its re-mark version and the inputs as JSON (the archive watermark, the cost sheet and the spread models it used). No figure of return.</summary>
public sealed record PhaseRunInputs(string RunId, string RemarkVersion, string InputsJson);

/// <summary>The identity of a stored cash run the phase check needs: the engine that made it and the charge sheet it was costed on (null for gross).</summary>
public sealed record PhaseCashRun(string RunKey, string EngineVersion, string? CostsSheetId);

/// <summary>A stored plan item (<c>trading.plan_items</c>) or paper entry (<c>paper.entries</c>) whose cell key has an eighth part, by table and row id.</summary>
public sealed record NonCashCellRow(string Source, long Id);

/// <summary>
/// The extra reads of the P14 phase check (spec P14-17), select only: every read is bounded by an id list or a limit and returns versions, ids, instants and words, never a price, IV, Greek, bar, quote or return figure.
/// The stored verdicts themselves come through <c>IExtensionEvaluationReader</c>.
/// </summary>
public interface IOptionsPhaseReader
{
    /// <summary>The most ids one read takes.</summary>
    public const int MaxIds = 200;

    /// <summary>The headers of the expression runs with these ids (at most <see cref="MaxIds"/>).</summary>
    ValueTask<IReadOnlyList<PhaseRunInputs>> RunInputsAsync(IReadOnlyCollection<string> runIds, CancellationToken cancellationToken);

    /// <summary>When the fetch-log rows with these ids ended (at most <see cref="MaxIds"/>); an id with no row is absent.</summary>
    ValueTask<IReadOnlyDictionary<long, DateTimeOffset>> FetchEndedAtAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken);

    /// <summary>When the newest finished fetch (outcome ok, empty or no_data) ended; null when none is stored.</summary>
    ValueTask<DateTimeOffset?> LatestFetchEndedAtAsync(CancellationToken cancellationToken);

    /// <summary>Whether the newest row of the chain-recorder switch says ON (no row is OFF).</summary>
    ValueTask<bool> IsChainRecorderOnAsync(CancellationToken cancellationToken);

    /// <summary>Whether the newest row of the backfill switch says ON (no row is OFF).</summary>
    ValueTask<bool> IsBackfillOnAsync(CancellationToken cancellationToken);

    /// <summary>Plan items and paper entries whose cell key has an eighth part, oldest row first, at most <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyList<NonCashCellRow>> NonCashCellRowsAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>The rates hash of a stored charge sheet, read alone: the current hash a stored verdict's sheet is compared with.</summary>
public interface ICostSheetHashes
{
    /// <summary>The SHA-256 of the rates of the stored sheet, or null when the sheet is not stored or no longer parses.</summary>
    ValueTask<string?> RatesHashAsync(string sheetId, CancellationToken cancellationToken);
}

/// <summary>The engine and charge sheet of one stored cash run, read alone through the result store.</summary>
public interface ICashRunHeaders
{
    /// <summary>The header of the stored cash run with the key, or null when none is stored.</summary>
    ValueTask<PhaseCashRun?> FindAsync(string runKey, CancellationToken cancellationToken);
}

/// <summary>The id of the newest stored half-spread model of an underlying, read alone through the model store.</summary>
public interface ISpreadModelIds
{
    /// <summary>The model id, or null when the underlying has no stored model.</summary>
    ValueTask<string?> LatestAsync(Guid underlyingId, CancellationToken cancellationToken);
}
