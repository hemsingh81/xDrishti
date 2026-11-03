namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The stored records of one family, newest first, with the versions in force now (P15-12). <see cref="Readable"/> is false when the store of the family could not be read (a missing table included): the family is then NOT EVALUABLE, not an error.</summary>
/// <param name="Family">The ledger family, for example <c>ext:ev-1</c>.</param>
/// <param name="Readable">Whether the records could be read.</param>
/// <param name="Records">The family's records, newest first (for the event filter: those of the window N of the effective switch).</param>
/// <param name="Current">The versions in force now by key; a record whose recorded value differs, or is not recorded, is stale.</param>
public sealed record EventsFamilyFacts(string Family, bool Readable, IReadOnlyList<EventsRecordFact> Records, IReadOnlyDictionary<string, string> Current);
