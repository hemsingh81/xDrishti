namespace XDrishti.Domain.Lab;

/// <summary>The out-of-sample trades of one calendar year (spec P5-05, assumption 7); the profit factor is null when the year has no loss.</summary>
public sealed record YearRow(int Year, int Trades, double MeanR, double TotalR, double? ProfitFactor, bool Positive);
