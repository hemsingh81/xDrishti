namespace XDrishti.Application.Portfolio.Returns;

/// <summary>How closely the history is sampled: every trading day, or the last trading day of each week (Monday to Sunday) or calendar month.</summary>
public enum HistoryResolution
{
    Day,
    Week,
    Month,
}
