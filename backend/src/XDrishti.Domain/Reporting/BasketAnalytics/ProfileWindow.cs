namespace XDrishti.Domain.Reporting;

/// <summary>The window a basket profile is computed over, both ends included (BK-06).</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day; nothing after it is read.</param>
public sealed record ProfileWindow(DateOnly From, DateOnly To);
