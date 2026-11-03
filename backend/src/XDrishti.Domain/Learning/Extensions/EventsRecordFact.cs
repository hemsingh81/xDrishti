namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One stored P15 evaluation as the phase check reads it (P15-12). <see cref="Versions"/> are the versions the record names (catalog, rule, parser, engine, lag); a key that is not recorded is simply absent.</summary>
/// <param name="Id">The record id.</param>
/// <param name="Protocol">F, X, P or L.</param>
/// <param name="Verdict">KEEP, DROP, INCONCLUSIVE or NOT_EVALUABLE.</param>
/// <param name="HoldOutRead">Whether this record is the one reading of the hold-out.</param>
/// <param name="Trials">N: the distinct trials of the family when the record was made.</param>
/// <param name="Rows">The rows (trades, events, candidates) the verdict rests on.</param>
/// <param name="Missing">The named missing quantities.</param>
/// <param name="Versions">The recorded versions by key.</param>
public sealed record EventsRecordFact(string Id, string Protocol, string Verdict, bool HoldOutRead, int Trials, int Rows, IReadOnlyList<string> Missing, IReadOnlyDictionary<string, string> Versions);
