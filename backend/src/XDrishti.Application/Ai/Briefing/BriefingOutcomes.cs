using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>How a service run reports a build: a missing plan, a missing digest and a deferred start are not failures.</summary>
internal static class BriefingOutcomes
{
    public static ServiceOutcome Of(Result<BriefingView> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            var v = result.Value;
            return ServiceOutcome.Succeeded(v.Repeated
                ? $"The {v.Kind} text of {v.Subject} was stored already; nothing written."
                : $"The {v.Kind} text of {v.Subject} stored (id {v.Id}, source {v.Source}{(v.Reason is null ? string.Empty : ", " + v.Reason)}).");
        }

        return result.Error.Code is BriefingErrors.NotFoundCode or BriefingErrors.NotEvaluableCode or BriefingErrors.DeferredCode
            ? ServiceOutcome.Succeeded(result.Error.Message)
            : ServiceOutcome.Failed(result.Error.Message);
    }
}
