namespace XDrishti.Domain.Lab;

/// <summary>The mean block length of the stationary bootstrap, in sessions, and a note when it was forced to the minimum.</summary>
public sealed record BlockLength(int Value, string? Note);
