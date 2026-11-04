namespace XDrishti.Domain.Options.Evaluation;

/// <summary>What protocol O is given for the development verdict (P14-09): the paired trades read from the out-of-sample windows before the hold-out, the unpaired counts by reason over the same windows, the ledger count N, the first day of the latest rule epoch and the first day of the hold-out (a date only).</summary>
/// <param name="Pairs">The paired trades dated before the hold-out; any order.</param>
/// <param name="Unpaired">The unpaired trades of the same windows by reason (fate word); reasons not listed count 0.</param>
/// <param name="Trials">N, the distinct trials of the family in the ledger (at least 1).</param>
/// <param name="LatestEpochFrom">The first day of the latest rule epoch (<c>nifty_expiry_tuesday</c>).</param>
/// <param name="HoldOutStart">The first day of the hold-out, or null when there is none.</param>
/// <param name="Options">The settings.</param>
/// <param name="Wording">The words of the protocol (O by default; R for the condor).</param>
/// <param name="LastPaired">The last entry date of any paired trade (a date only), or null; it lets the record say that every paired trade lies in the hold-out.</param>
public sealed record ProtocolOInput(IReadOnlyList<ExpressionPair> Pairs, IReadOnlyDictionary<string, int> Unpaired, int Trials, DateOnly LatestEpochFrom, DateOnly? HoldOutStart, ExpressionStudyOptions Options, DateOnly? LastPaired = null, ProtocolWording? Wording = null);
