namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Thrown by a stream returned from <see cref="IFilingsSource.OpenAsync"/> when the bytes read do not hash to the listed SHA-256.</summary>
public sealed class FilingContentChangedException() : IOException("content_changed");
