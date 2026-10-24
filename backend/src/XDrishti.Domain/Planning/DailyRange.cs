namespace XDrishti.Domain.Planning;

/// <summary>The high, low and close of one closed session, rupees (oldest first in a series).</summary>
public sealed record DailyRange(double High, double Low, double Close);
