using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>The session a plan is built for (spec P7-08).</summary>
/// <param name="Session">The last closed session; the data is as of its close, nothing later.</param>
/// <param name="ForDate">The session the plan is for (the next one).</param>
/// <param name="ReviewCutoff">The review cutoff of <paramref name="ForDate"/>.</param>
/// <param name="Now">The instant of the run.</param>
public sealed record PlanBuildRequest(DateOnly Session, DateOnly ForDate, DateTimeOffset ReviewCutoff, DateTimeOffset Now);

/// <summary>
/// The stage that turns a green set of gates into a plan to publish (spec P7-08). It only builds: storing is the pipeline's, after the builder returned a whole plan, so a builder that fails never leaves a partial plan.
/// The only implementation today is the null builder (an empty plan marked skeleton); the real one is P7-04/P7-09. Nothing behind this port may place, modify or cancel an order.
/// </summary>
public interface IPlanBuilder
{
    /// <summary>A short id for the plan key, for example <c>skeleton</c>.</summary>
    string Id { get; }

    /// <summary>Builds the plan, or says why it cannot (then nothing is published).</summary>
    ValueTask<Result<NewPlan>> BuildAsync(PlanBuildRequest request, CancellationToken cancellationToken);
}
