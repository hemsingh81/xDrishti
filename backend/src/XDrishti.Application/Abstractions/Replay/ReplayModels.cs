using XDrishti.Domain.Paper;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Abstractions.Replay;

/// <summary>A run to store (SR-01 part B). The texts are the canonical JSON of the parameters and of the settings snapshot; the hashes are those of <c>ReplayHashes</c>.</summary>
/// <param name="Id">The run id.</param>
/// <param name="From">The first session (D).</param>
/// <param name="To">The last session (E).</param>
/// <param name="Mode">frozen or learning.</param>
/// <param name="ParamsJson">The parameters.</param>
/// <param name="SettingsJson">The one settings snapshot of the run.</param>
/// <param name="VersionsJson">The strategy versions the run may use (strategy id to versions), stored so a resumed run never reads today's active version.</param>
/// <param name="LearningSources">The sources learning mode recomputes from, taken at the start; null in frozen mode.</param>
/// <param name="InputHash">The hash of everything the run reads.</param>
/// <param name="ScopeHash">The hash of the scope as it stood at the start.</param>
/// <param name="SessionsTotal">The trading sessions from D to E.</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record NewReplayRun(Guid Id, DateOnly From, DateOnly To, string Mode, string ParamsJson, string SettingsJson, string VersionsJson, string? LearningSources, string InputHash, string ScopeHash, int SessionsTotal, DateTimeOffset CreatedAt);

/// <summary>One stored run with how far it has got. <see cref="Complete"/> is true only for a run whose every session was replayed.</summary>
/// <param name="Id">The run id.</param>
/// <param name="PairId">The id the frozen and learning runs of one request share; null for a single run.</param>
/// <param name="From">D.</param>
/// <param name="To">E.</param>
/// <param name="Mode">frozen or learning.</param>
/// <param name="ParamsJson">The parameters.</param>
/// <param name="SettingsJson">The settings snapshot.</param>
/// <param name="VersionsJson">The stored strategy version map.</param>
/// <param name="LearningSources">The stored sources of learning mode; null in frozen mode.</param>
/// <param name="InputHash">The input hash.</param>
/// <param name="ScopeHash">The scope hash.</param>
/// <param name="ResultHash">The result hash; null until the run completes.</param>
/// <param name="State">See <see cref="ReplayRunStates"/>.</param>
/// <param name="CursorSession">The last session stored (the as-of session); null before the first.</param>
/// <param name="SessionsDone">Sessions stored.</param>
/// <param name="SessionsTotal">Sessions in the range.</param>
/// <param name="TrialNumber">1 for the first run over this range, 2 for the next (AC 12): how many replays have looked at the same range, this one included.</param>
/// <param name="CreatedAt">When it was stored.</param>
/// <param name="FinishedAt">When it ended; null while active.</param>
/// <param name="FailReason">Why a failed run failed (the session and the reason); null otherwise.</param>
public sealed record ReplayRunRow(
    Guid Id,
    Guid? PairId,
    DateOnly From,
    DateOnly To,
    string Mode,
    string ParamsJson,
    string SettingsJson,
    string VersionsJson,
    string? LearningSources,
    string InputHash,
    string ScopeHash,
    string? ResultHash,
    string State,
    DateOnly? CursorSession,
    int SessionsDone,
    int SessionsTotal,
    int TrialNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? FailReason)
{
    /// <summary>Every session was replayed (AC 14: <c>complete: false</c> while the job runs, and for a cancelled or failed run).</summary>
    public bool Complete => State == ReplayRunStates.Completed;
}

/// <summary>The outcome of storing runs: started, or refused because a replay is active (the active runs are returned).</summary>
/// <param name="Started">Whether the runs were stored.</param>
/// <param name="Runs">The stored runs, or the active ones that refused the start.</param>
public sealed record ReplayInsert(bool Started, IReadOnlyList<ReplayRunRow> Runs);

/// <summary>One replayed session as stored: the record, and the equity and drawdown at its close.</summary>
/// <param name="Session">The session.</param>
/// <param name="RecordJson">The session record (canonical JSON).</param>
/// <param name="Equity">Equity at the close.</param>
/// <param name="Drawdown">Drawdown in percent; null without a basis.</param>
/// <param name="RecordSha256">The hash of the record text, which the result hash is made of.</param>
public sealed record ReplaySessionRow(DateOnly Session, string RecordJson, decimal Equity, decimal? Drawdown, string RecordSha256);

/// <summary>One trade of the replay book as stored. A trade is stored when it closes, or at the end of a run that stops with it open (then the exit columns are empty).</summary>
public sealed record ReplayTradeRow(
    long TradeNo,
    string Symbol,
    string Side,
    string CellKey,
    int Qty,
    DateOnly EntrySession,
    DateTimeOffset EntryAt,
    decimal EntryPrice,
    decimal StopPrice,
    bool Swing,
    string TradeKey,
    DateOnly? ExitSession,
    DateTimeOffset? ExitAt,
    decimal? GrossPnl,
    decimal? Charges,
    decimal? NetPnl,
    string? ChargeSheetId,
    double? RSim);

/// <summary>The summary figures of a run: the run itself and the figures <c>PaperLedger</c> gives over its stored trades at the as-of session.</summary>
/// <param name="Run">The run.</param>
/// <param name="Figures">The ledger figures; equity, open risk and the realised series come only from the paper ledger.</param>
/// <param name="Trades">Trades stored.</param>
public sealed record ReplaySummary(ReplayRunRow Run, LedgerFigures Figures, int Trades);
