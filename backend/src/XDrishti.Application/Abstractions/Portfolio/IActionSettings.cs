using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The thresholds in force, and who stored them when (both null while the documented defaults apply).</summary>
public sealed record ActionThresholdSetting(ActionThresholds Values, string? UpdatedBy, DateTimeOffset? UpdatedAt);

/// <summary>The thresholds of the Action-center rules (ADR 0022), one <c>platform.app_settings</c> row.</summary>
public interface IActionSettings
{
    /// <summary>The stored thresholds, or the defaults while none are stored or the stored ones are unreadable or out of range.</summary>
    ValueTask<ActionThresholdSetting> GetAsync(CancellationToken cancellationToken);

    /// <summary>Collects the change (and nothing else): <see cref="IPortfolioUnitOfWork"/> saves it together with the audit event.</summary>
    ValueTask SetAsync(ActionThresholds thresholds, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
