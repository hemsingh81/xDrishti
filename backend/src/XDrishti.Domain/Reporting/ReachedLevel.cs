namespace XDrishti.Domain.Reporting;

/// <summary>How many trades had a favourable excursion of at least <paramref name="AtR"/>, and the share of those with a recorded excursion.</summary>
public sealed record ReachedLevel(double AtR, int Count, double Share);
