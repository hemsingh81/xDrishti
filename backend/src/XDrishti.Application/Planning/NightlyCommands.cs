using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>
/// Runs the nightly pipeline once (spec P7-08): reads the gates in order, and only when all pass builds and publishes the plan for the next session. A failing or unavailable gate publishes nothing and records one failure row.
/// A session that already has a published plan is not touched again (the result is the first one). It cannot place an order or approve anything.
/// </summary>
/// <param name="Date">The closed session to plan from; null for the last closed session.</param>
/// <param name="DryRun">Read the gates and build, write nothing.</param>
/// <param name="Manual">Whether a person started it (the phase check counts these).</param>
/// <param name="RequestedBy">Who or what started it, for the log of the caller; not stored.</param>
public sealed record RunNightlyPlanCommand(DateOnly? Date, bool DryRun, bool Manual, string RequestedBy) : ICommand;

/// <summary>The reading of one gate in a result.</summary>
/// <param name="Gate">The gate id.</param>
/// <param name="Status">pass, fail or unavailable.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record NightlyGateView(string Gate, string Status, string Reason);

/// <summary>What one run did (spec P7-08).</summary>
/// <param name="Session">The closed session the plan rests on.</param>
/// <param name="ForDate">The session the plan is for.</param>
/// <param name="Outcome"><c>published</c>, <c>failed_closed</c>, or <c>would_publish</c> (a dry run whose gates are all green).</param>
/// <param name="Repeated">True when the session already had a published plan and nothing was done.</param>
/// <param name="DryRun">Whether nothing was written because it was a dry run.</param>
/// <param name="Attempt">The attempt number of this session (the stored one when repeated; the next one in a dry run).</param>
/// <param name="FailedGate">The gate or stage that blocked, or null.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="PlanId">The published plan, or null.</param>
/// <param name="Gates">The gates read in this run, in order (empty when repeated).</param>
public sealed record NightlyRunView(DateOnly Session, DateOnly ForDate, string Outcome, bool Repeated, bool DryRun, int Attempt, string? FailedGate, string Reason, long? PlanId, IReadOnlyList<NightlyGateView> Gates);

/// <summary>The recorded attempts of the pipeline, newest session first (spec P7-08).</summary>
/// <param name="Date">Only the runs planning for this session, or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListPlanRunsQuery(DateOnly? Date, int Limit) : IQuery<Result<IReadOnlyList<PlanRunView>>>;

/// <summary>One recorded attempt.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Attempt">1 for the first attempt of the planned session.</param>
/// <param name="ForDate">The session planned for.</param>
/// <param name="PlanKey">The plan key when a plan was published.</param>
/// <param name="Outcome">published or failed_closed.</param>
/// <param name="FailedGate">The gate that blocked, or null.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="PlanId">The plan published, or null.</param>
/// <param name="Manual">Whether a person started it.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
public sealed record PlanRunView(long Id, int Attempt, DateOnly ForDate, string? PlanKey, string Outcome, string? FailedGate, string Reason, long? PlanId, bool Manual, DateTimeOffset StartedAt, DateTimeOffset FinishedAt)
{
    internal static PlanRunView Of(PlanRunRow row) =>
        new(row.Id, row.Attempt, row.Run.ForDate, row.Run.PlanKey, row.Run.Outcome, row.Run.FailedGate, row.Run.Reason, row.Run.PlanId, row.Run.Manual, row.Run.StartedAt, row.Run.FinishedAt);
}

/// <summary>The outcomes stored in <c>plan_runs.outcome</c> and shown by a run.</summary>
internal static class NightlyOutcomes
{
    public const string Published = "published";

    public const string FailedClosed = "failed_closed";

    public const string WouldPublish = "would_publish";
}
