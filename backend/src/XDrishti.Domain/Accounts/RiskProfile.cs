using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>A shared set of risk limits that trading accounts refer to. The seeded profiles (Standard, Conservative) cannot be deleted.</summary>
public sealed class RiskProfile : Entity<Guid>
{
    public const int MaxNameLength = 60;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private RiskProfile(
        Guid id,
        string name,
        bool isSeeded,
        DateTimeOffset createdAt,
        decimal riskPerTradePct,
        decimal maxCapitalPerTradePct,
        int maxNewEntriesPerDay,
        int maxOpenPositions,
        decimal maxTotalRiskPct,
        int maxPerSector,
        decimal maxPairCorrelation,
        int maxIntraday,
        int maxSwing,
        decimal dailyLossR,
        decimal dailyLossPct,
        decimal weeklyLossR,
        WeeklyLossAction weeklyLossAction)
        : base(id)
    {
        Name = name;
        IsSeeded = isSeeded;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        RiskPerTradePct = riskPerTradePct;
        MaxCapitalPerTradePct = maxCapitalPerTradePct;
        MaxNewEntriesPerDay = maxNewEntriesPerDay;
        MaxOpenPositions = maxOpenPositions;
        MaxTotalRiskPct = maxTotalRiskPct;
        MaxPerSector = maxPerSector;
        MaxPairCorrelation = maxPairCorrelation;
        MaxIntraday = maxIntraday;
        MaxSwing = maxSwing;
        DailyLossR = dailyLossR;
        DailyLossPct = dailyLossPct;
        WeeklyLossR = weeklyLossR;
        WeeklyLossAction = weeklyLossAction;
    }

    public string Name { get; private set; }

    /// <summary>Created by the system (not by the owner): cannot be deleted.</summary>
    public bool IsSeeded { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public decimal RiskPerTradePct { get; private set; }

    public decimal MaxCapitalPerTradePct { get; private set; }

    public int MaxNewEntriesPerDay { get; private set; }

    public int MaxOpenPositions { get; private set; }

    public decimal MaxTotalRiskPct { get; private set; }

    public int MaxPerSector { get; private set; }

    public decimal MaxPairCorrelation { get; private set; }

    public int MaxIntraday { get; private set; }

    public int MaxSwing { get; private set; }

    public decimal DailyLossR { get; private set; }

    public decimal DailyLossPct { get; private set; }

    public decimal WeeklyLossR { get; private set; }

    public WeeklyLossAction WeeklyLossAction { get; private set; }

    public RiskLimits Limits => new(
        RiskPerTradePct, MaxCapitalPerTradePct, MaxNewEntriesPerDay, MaxOpenPositions, MaxTotalRiskPct, MaxPerSector,
        MaxPairCorrelation, MaxIntraday, MaxSwing, DailyLossR, DailyLossPct, WeeklyLossR, WeeklyLossAction);

    /// <param name="name">Required, at most 60 characters.</param>
    /// <param name="limits">The values; every one is range-checked.</param>
    /// <param name="now">Creation time.</param>
    /// <param name="isSeeded">Only for profiles the system itself provides (they cannot be deleted); owners' profiles are never seeded.</param>
    public static Result<RiskProfile> Create(string? name, RiskLimits limits, DateTimeOffset now, bool isSeeded = false)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var errors = Validate(name, limits, out var cleanName);
        if (errors.HasErrors)
        {
            return Result.Failure<RiskProfile>(AccountErrors.Invalid(errors));
        }

        return Result.Success(new RiskProfile(
            Guid.CreateVersion7(now), cleanName, isSeeded, now,
            limits.RiskPerTradePct, limits.MaxCapitalPerTradePct, limits.MaxNewEntriesPerDay, limits.MaxOpenPositions, limits.MaxTotalRiskPct,
            limits.MaxPerSector, limits.MaxPairCorrelation, limits.MaxIntraday, limits.MaxSwing, limits.DailyLossR, limits.DailyLossPct,
            limits.WeeklyLossR, limits.WeeklyLossAction));
    }

    public Result Update(string? name, RiskLimits limits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var errors = Validate(name, limits, out var cleanName);
        if (errors.HasErrors)
        {
            return Result.Failure(AccountErrors.Invalid(errors));
        }

        Name = cleanName;
        RiskPerTradePct = limits.RiskPerTradePct;
        MaxCapitalPerTradePct = limits.MaxCapitalPerTradePct;
        MaxNewEntriesPerDay = limits.MaxNewEntriesPerDay;
        MaxOpenPositions = limits.MaxOpenPositions;
        MaxTotalRiskPct = limits.MaxTotalRiskPct;
        MaxPerSector = limits.MaxPerSector;
        MaxPairCorrelation = limits.MaxPairCorrelation;
        MaxIntraday = limits.MaxIntraday;
        MaxSwing = limits.MaxSwing;
        DailyLossR = limits.DailyLossR;
        DailyLossPct = limits.DailyLossPct;
        WeeklyLossR = limits.WeeklyLossR;
        WeeklyLossAction = limits.WeeklyLossAction;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>A seeded profile, or one that accounts still use, cannot be deleted.</summary>
    public Result CheckCanBeDeleted(int accountsUsingIt)
    {
        if (IsSeeded)
        {
            return Result.Failure(AccountErrors.Conflict("The built-in risk profiles cannot be deleted."));
        }

        return accountsUsingIt > 0
            ? Result.Failure(AccountErrors.Conflict($"The risk profile is used by {accountsUsingIt} account(s). Move them to another profile first."))
            : Result.Success();
    }

    private static FieldErrors Validate(string? name, RiskLimits limits, out string cleanName)
    {
        var errors = limits.Validate();
        cleanName = (name ?? string.Empty).Trim();
        if (cleanName.Length is 0 or > MaxNameLength || cleanName.Any(char.IsControl))
        {
            errors.Add("name", $"Required, at most {MaxNameLength} characters.");
        }

        return errors;
    }
}
