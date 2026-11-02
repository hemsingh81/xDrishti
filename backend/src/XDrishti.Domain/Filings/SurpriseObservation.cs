namespace XDrishti.Domain.Filings;

/// <summary>The surprise numbers of one results filing (P15-09): <c>sue_yoy</c> and <c>results_car3</c> as they were computable at the close of its reaction session, NaN where unknown (never zero, never filled).</summary>
/// <param name="InstrumentId">The instrument.</param>
/// <param name="PeriodEnd">The quarter end the filing reports; its calendar quarter is the season.</param>
/// <param name="BroadcastTs">The exchange broadcast time.</param>
/// <param name="Sue">The standardised unexpected earnings of the filing, or NaN.</param>
/// <param name="Car3">The three-session cumulative abnormal return ending at the close of the reaction session, or NaN.</param>
/// <param name="Unverified">The EPS facts behind <paramref name="Sue"/> come from a parser that has not passed real filings (labelled, counted, never silently trusted).</param>
public sealed record SurpriseObservation(Guid InstrumentId, DateOnly PeriodEnd, DateTimeOffset BroadcastTs, double Sue, double Car3, bool Unverified = false);
