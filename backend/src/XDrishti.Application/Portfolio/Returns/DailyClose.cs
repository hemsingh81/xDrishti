namespace XDrishti.Application.Portfolio.Returns;

/// <summary>The close of an instrument on an India date, already adjusted for corporate actions (see <see cref="AdjustedCloses"/>). Closes are positive.</summary>
public readonly record struct DailyClose(DateOnly Date, decimal Close);
