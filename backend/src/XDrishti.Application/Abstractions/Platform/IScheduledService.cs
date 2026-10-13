using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// A job the scheduler runs (ADR 0018 §1, §3): registered by the module that owns the work, one implementation per service. It only carries out one run; <b>when</b> it runs, the retries, the
/// timeout, the catch-up and the history belong to the scheduler. Implementations are singletons and open their own scopes. Expected problems are an outcome (failed, skipped), not an exception.
/// </summary>
public interface IScheduledService
{
    ServiceDefinition Definition { get; }

    /// <summary>The settings a service starts with when it has no stored row yet, taken from deployment configuration (a first-start seed); null to start on the definition's defaults.</summary>
    ServiceSettings? FirstStartSettings();

    /// <summary>Carries out one run. Cancelled when the timeout passes or the application stops; then it should return promptly.</summary>
    ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken);
}
