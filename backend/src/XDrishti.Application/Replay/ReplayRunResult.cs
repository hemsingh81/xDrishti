using XDrishti.Domain.Paper;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>The result of a whole in-memory replay (SR-01 part A; part B stores it).</summary>
/// <param name="Sessions">One record per replayed session, in order.</param>
/// <param name="Book">The replay book at the end.</param>
/// <param name="Final">The ledger figures at the close of the last session.</param>
/// <param name="Labels">The hindsight labels of the run (the parameters' labels plus those any session added).</param>
internal sealed record ReplayRunResult(IReadOnlyList<ReplaySessionRecord> Sessions, ReplayBook Book, LedgerFigures Final, IReadOnlyList<string> Labels);
