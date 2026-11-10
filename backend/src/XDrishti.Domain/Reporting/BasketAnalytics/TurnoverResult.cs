namespace XDrishti.Domain.Reporting;

/// <summary>Member turnover and the basis of the membership history (BK-06 m13).</summary>
/// <param name="Turnover">(adds + removes) / average members, or why there is none.</param>
/// <param name="Basis"><see cref="MemberTurnover.Dated"/> or <see cref="MemberTurnover.SingleSnapshot"/>.</param>
/// <param name="FirstDate">The first date with membership history; null when there is none.</param>
/// <param name="Adds">Members added inside the window.</param>
/// <param name="Removes">Members removed inside the window.</param>
public sealed record TurnoverResult(Metric Turnover, string Basis, DateOnly? FirstDate, int Adds, int Removes);
