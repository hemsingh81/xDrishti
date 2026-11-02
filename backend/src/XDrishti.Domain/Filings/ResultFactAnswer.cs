namespace XDrishti.Domain.Filings;

/// <summary>One answered fact: its state (<c>value</c> or <c>unavailable</c>), the number or the reason, the unit and the filing it comes from with that filing's broadcast time. A fact never seen has no filing and the reason <c>not_in_file</c>.</summary>
public sealed record ResultFactAnswer(DateOnly PeriodEnd, string Field, string Span, string Basis, string State, decimal? Value, string Unit, string? Reason, Guid? FilingId, DateTimeOffset? BroadcastTs);
