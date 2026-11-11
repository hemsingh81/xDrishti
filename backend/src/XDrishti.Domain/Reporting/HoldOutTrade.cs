namespace XDrishti.Domain.Reporting;

/// <summary>One closed trade as the median split reads it (BK-07 m23).</summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="ExitDate">The India date of the exit.</param>
/// <param name="R">The trade's R on the basis of the report.</param>
public sealed record HoldOutTrade(string Strategy, DateOnly ExitDate, double R);
