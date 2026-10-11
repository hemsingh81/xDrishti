using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// The automatic refresh of an account's holdings: the mode and the time of day (India time) used by <see cref="PortfolioRefreshMode.Daily"/>.
/// The time is always valid, also while the mode is off.
/// </summary>
public sealed record PortfolioRefresh(PortfolioRefreshMode Mode, TimeOnly DailyTimeIst)
{
    /// <summary>After the market closes (15:30), so the daily run sees the day's final prices.</summary>
    public static readonly TimeOnly DefaultDailyTimeIst = new(16, 0);

    /// <summary>Off, with the daily time 16:00 — the state of a new account.</summary>
    public static PortfolioRefresh Default { get; } = new(PortfolioRefreshMode.Off, DefaultDailyTimeIst);

    public bool IsOn => Mode != PortfolioRefreshMode.Off;

    /// <summary>Checks the combination. Field name of a rejection: <c>refreshMode</c>.</summary>
    public static Result<PortfolioRefresh> Create(PortfolioRefreshMode mode, TimeOnly dailyTimeIst)
    {
        var errors = new FieldErrors();
        if (!Enum.IsDefined(mode))
        {
            errors.Add("refreshMode", "Use off or daily.");
        }

        return errors.HasErrors
            ? Result.Failure<PortfolioRefresh>(AccountErrors.Invalid(errors))
            : Result.Success(new PortfolioRefresh(mode, new TimeOnly(dailyTimeIst.Hour, dailyTimeIst.Minute)));
    }
}
