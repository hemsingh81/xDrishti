using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// The skeleton's builder (spec P7-08): an EMPTY plan, marked skeleton, provisional, with no ticket. It proves the pipeline end to end and publishes nothing a person could act on. The real builder is P7-04/P7-09.
/// </summary>
internal sealed class NullPlanBuilder : IPlanBuilder
{
    public const string BuilderId = PlanWording.SkeletonBasisPrefix;

    public const string Basis = PlanWording.SkeletonBasisPrefix + ": no tickets are built yet (the real builder is P7-04/P7-09)";

    public const string CreatedBy = "system:nightly-skeleton";

    public string Id => BuilderId;

    public ValueTask<Result<NewPlan>> BuildAsync(PlanBuildRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{BuilderId}|{request.Session:yyyy-MM-dd}|{request.ForDate:yyyy-MM-dd}")));
        var plan = new NewPlan(NightlyRules.PlanKey(request.ForDate, BuilderId), request.ForDate, request.ReviewCutoff, 1, hash, Basis, true, CreatedBy, request.Now, []);
        return ValueTask.FromResult(Result.Success(plan));
    }
}
