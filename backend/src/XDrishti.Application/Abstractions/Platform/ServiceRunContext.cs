using System.Text.Json;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Abstractions.Platform;

/// <summary>
/// What a service is told about the run it is asked to carry out: the run record, what started it (and who, for a manual run), which attempt it is (1, then one per retry), its parameters
/// (every one of the schema, defaults filled in) and a way to link a data job it started to the run record.
/// </summary>
public sealed class ServiceRunContext(Guid runId, ServiceRunTrigger trigger, int attempt, string requestedBy, IReadOnlyDictionary<string, JsonElement> parameters, Func<Guid, CancellationToken, ValueTask> linkDataJob)
{
    public Guid RunId { get; } = runId;

    public ServiceRunTrigger Trigger { get; } = trigger;

    public int Attempt { get; } = attempt;

    /// <summary>The owner's address for a manual run, otherwise a <c>system:…</c> name.</summary>
    public string RequestedBy { get; } = requestedBy;

    public IReadOnlyDictionary<string, JsonElement> Parameters { get; } = parameters;

    /// <summary>
    /// True once the service has handed its work to a background worker that this run cannot stop (a data job, a refresh). A run that then times out is recorded as failed with "the job is still running"
    /// and is not retried.
    /// </summary>
    public bool HandedOffToBackground { get; private set; }

    /// <summary>Says that the work now runs in a background worker that cannot be cancelled with this run (see <see cref="HandedOffToBackground"/>).</summary>
    public void MarkHandedOff() => HandedOffToBackground = true;

    /// <summary>Records the data job this run started in the run record (<c>data_job_id</c>), so that its progress can be read. Also marks the work as handed off.</summary>
    public ValueTask LinkDataJobAsync(Guid dataJobId, CancellationToken cancellationToken)
    {
        MarkHandedOff();
        return linkDataJob(dataJobId, cancellationToken);
    }
}
