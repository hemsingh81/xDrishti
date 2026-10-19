namespace XDrishti.Application.Abstractions.Research;

/// <summary>Raised by the stream of a stored run's trades when a row cannot be read (a stream cannot return a result); the failure is <see cref="StoreErrors.RowUnreadableCode"/>.</summary>
public sealed class StoreReadException(string message) : Exception(message);
