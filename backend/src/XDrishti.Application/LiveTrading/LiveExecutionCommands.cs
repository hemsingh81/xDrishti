using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Computes the executions of the sessions settled so far: one per entry outcome and one class per entry order (the last seven days, or one session). Idempotent; reads stored data only.</summary>
public sealed record ComputeLiveExecutionsCommand(DateOnly? Date, Guid? AccountId) : ICommand;

/// <summary>What the step did, counts only.</summary>
public sealed record LiveExecutionsOutcome(int Sessions, int ExecutionsWritten, int OrderClassesWritten, int Failed, string Summary);

/// <summary>Computes and stores the slippage estimates and the capacity scores of one week (default: the current week, Monday as its start). Idempotent per week and key.</summary>
public sealed record ComputeLiveEstimatesCommand(DateOnly? WeekStart) : ICommand;

/// <summary>What the estimates step did, counts only.</summary>
public sealed record LiveEstimatesOutcome(DateOnly WeekStart, int Executions, int SlippageRows, int SlippageWritten, int CapacityRows, int CapacityWritten, string Summary);
