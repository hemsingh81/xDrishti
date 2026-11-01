using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>One stored result: the session, the underlying, the source (<c>chain</c> or <c>rolling</c>), the fetch key of a rolling series, and the check.</summary>
public sealed record OptionQualityRow(DateOnly SessionDate, Guid UnderlyingId, string Source, DateOnly? Expiry, string? FetchKey, QualityCheck Check);
