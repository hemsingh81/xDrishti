using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>What starting a replay on a stored run would mean (read only).</summary>
/// <param name="BaselineRunKey">The stored run.</param>
public sealed record GetExperimentPreviewQuery(string BaselineRunKey) : IQuery<Result<ExperimentPreviewView>>;

/// <summary>
/// The owner's start of ONE replay job (spec P8-18 Part B, ADR 0129). The actor must be <c>owner:...</c> and must come from the signed-in session after a fresh password (the endpoint asks); it is never a field of a body. The knob is one entry-neutral exit parameter or
/// slippage upward; nothing here applies anything.
/// </summary>
/// <param name="BaselineRunKey">The stored run to change one thing of.</param>
/// <param name="KnobKind"><c>exit</c> or <c>slippage</c>.</param>
/// <param name="KnobName">The knob name from the preview.</param>
/// <param name="CandidateValue">The new value.</param>
/// <param name="HoldOutMonths">Months kept out; null is 6.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
/// <param name="Actor">The owner.</param>
public sealed record StartExperimentJobCommand(string BaselineRunKey, string? KnobKind, string? KnobName, double? CandidateValue, int? HoldOutMonths, string? Reason, string Actor) : ICommand;

/// <summary>Stops a job: a queued one at once, a running one at its next safe point. The safe direction: it needs no password and no capability.</summary>
/// <param name="Id">The job.</param>
/// <param name="Actor">The owner.</param>
public sealed record CancelExperimentJobCommand(Guid Id, string Actor) : ICommand;

/// <summary>One job.</summary>
public sealed record GetExperimentJobQuery(Guid Id) : IQuery<Result<ExperimentJobView>>;

/// <summary>The newest jobs.</summary>
public sealed record ListExperimentJobsQuery(int Limit) : IQuery<Result<ExperimentJobList>>;
