using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// The owner's start of one replay job (spec P8-18 Part B, ADR 0129). The order of the refusals is fixed: the capability (the gate says OFF before anything is read), the owner and the input, the stored baseline, the proof that it can be replayed, the knob rules, then under one lock the
/// one-at-a-time rule and the daily limit. Only then a row is written, queued; this handler runs nothing and applies nothing. The job is run by the dispatcher.
/// </summary>
internal sealed class StartExperimentJobHandler(
    ICapabilityGate gate,
    IRunStore runs,
    ReplayPlanner planner,
    IExperimentJobStore jobs,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    ExperimentJobSettings settings,
    TimeProvider clock,
    ILogger<StartExperimentJobHandler> logger) : ICommandHandler<StartExperimentJobCommand, StartedExperimentJob>
{
    private const int DefaultHoldOutMonths = 6;

    public async ValueTask<Result<StartedExperimentJob>> HandleAsync(StartExperimentJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var open = await gate.RequireAsync(Domain.Capabilities.Capabilities.ExperimentsUi, cancellationToken);
        if (open.IsFailure)
        {
            return Refuse(open.Error);
        }

        var problems = Check(command);
        if (problems is not null)
        {
            return Refuse(problems);
        }

        var header = await runs.FindAsync(command.BaselineRunKey, cancellationToken);
        var record = header is null ? null : await runs.ReadRecordTextAsync(command.BaselineRunKey, cancellationToken);
        if (header is null || record is null)
        {
            return Refuse(LearningErrors.RunNotFound(command.BaselineRunKey));
        }

        var plan = await planner.PlanAsync(header, record, cancellationToken);
        if (plan.IsFailure)
        {
            return Refuse(plan.Error);
        }

        var knob = ReplayKnobCatalogue.Check(command.KnobKind, command.KnobName, command.CandidateValue!.Value, plan.Value.Baseline.Exit, plan.Value.Baseline.Fills.Slippage);
        if (knob.IsFailure)
        {
            return Refuse(knob.Error);
        }

        var months = command.HoldOutMonths ?? DefaultHoldOutMonths;
        var now = clock.GetUtcNow();
        var key = JobKey(command.BaselineRunKey, knob.Value, months);
        var newJob = new NewExperimentJob(Guid.CreateVersion7(now), key, command.BaselineRunKey, command.KnobKind!, knob.Value.Knob.Name, knob.Value.Knob.BaselineValue, knob.Value.Knob.CandidateValue, months, command.Reason!.Trim(), command.Actor, plan.Value.Baseline.InstrumentIds.Count, now);
        var inserted = await jobs.InsertAsync(newJob, settings.MaxJobsPerDay, ExperimentJobSettings.Window, cancellationToken);
        switch (inserted.Status)
        {
            case ExperimentJobInsertStatus.Active:
                return Refuse(ExperimentJobErrors.Active(inserted.Job!.Id));
            case ExperimentJobInsertStatus.DailyLimit:
                var leaves = inserted.OldestLeavesAt ?? now;
                return Refuse(ExperimentJobErrors.DailyLimit(settings.MaxJobsPerDay, leaves, (int)Math.Max(1, Math.Ceiling((leaves - now).TotalSeconds))));
            case ExperimentJobInsertStatus.CapabilityOff:
                return Refuse(XDrishti.Domain.Capabilities.CapabilityErrors.Off(XDrishti.Domain.Capabilities.Capabilities.ExperimentsUi));
            case ExperimentJobInsertStatus.Existing:
                return Result.Success(new StartedExperimentJob(false, ExperimentJobViews.Of(inserted.Job!, header)));
            default:
                break;
        }

        var job = inserted.Job!;
        ExperimentJobLogEvents.Queued(logger, job.Id, command.KnobKind!, job.Instruments);
        events.Record(AccountEvent.Record(AccountEventType.ExperimentJobStarted, null, string.Create(CultureInfo.InvariantCulture, $"kind={command.KnobKind} instruments={job.Instruments}"), command.Actor, now));
        await unitOfWork.TrySaveChangesAsync(cancellationToken);
        return Result.Success(new StartedExperimentJob(true, ExperimentJobViews.Of(job, header)));
    }

    /// <summary>The job key: the same baseline, knob, candidate value and hold-out are the same job (the identical input returns it).</summary>
    internal static string JobKey(string baselineRunKey, CheckedKnob knob, int holdOutMonths) =>
        CanonicalText.Sha256Hex(string.Create(CultureInfo.InvariantCulture, $"p8-18|{baselineRunKey}|{knob.Knob.Kind}|{knob.Knob.Name}|{CanonicalText.Number(knob.Candidate)}|{holdOutMonths}"));

    private static Error? Check(StartExperimentJobCommand command)
    {
        if (!OwnerConfirmation.IsOwnerActor(command.Actor) || command.Actor.Length > 100)
        {
            return Error.Forbidden("experiments.owner_only", "Only the signed-in owner starts a replay.");
        }

        var fields = new List<FieldError>();
        if (!RunRecordText.IsHex64(command.BaselineRunKey))
        {
            fields.Add(new FieldError("baselineRunKey", "A run key is 64 lower-case hex characters."));
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500 || reason.Any(char.IsControl))
        {
            fields.Add(new FieldError("reason", "A start needs a reason of 1 to 500 characters without control characters."));
        }

        if (command.CandidateValue is not { } value || !double.IsFinite(value))
        {
            fields.Add(new FieldError("knob.candidateValue", "The candidate value must be a finite number."));
        }

        if (command.HoldOutMonths is < 0 or > 120)
        {
            fields.Add(new FieldError("holdOutMonths", "Hold-out months are 0 to 120."));
        }

        return fields.Count == 0 ? null : LearningErrors.Invalid(fields);
    }

    private Result<StartedExperimentJob> Refuse(Error error)
    {
        ExperimentJobLogEvents.Refused(logger, error.Code);
        return Result.Failure<StartedExperimentJob>(error);
    }
}
