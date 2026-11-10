using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>The expected failures of training from the simulation (spec AU-13).</summary>
public static class SimTrainingErrors
{
    public const string DisabledCode = "sim_training.disabled";
    public const string JobNotFoundCode = "sim_training.job_not_found";
    public const string JobNotFinishedCode = "sim_training.job_not_finished";
    public const string NotFoundCode = "sim_training.not_found";

    /// <summary>The owner has not turned training on: 409.</summary>
    public static Error Disabled => Error.Conflict(DisabledCode, "Training from the simulation is off; the owner turns Training:Enabled on.");

    /// <summary>No job with this id: 404.</summary>
    public static Error JobNotFound(Guid id) => Error.NotFound(JobNotFoundCode, "There is no simulate-everything job " + id.ToString("D") + ".");

    /// <summary>The job has not completed (running, paused or cancelled): 409.</summary>
    public static Error JobNotFinished(Guid id, string state) => Error.Conflict(JobNotFinishedCode, "The job " + id.ToString("D") + " is " + state + "; training reads a completed job only.");

    /// <summary>The job has no training yet: 404.</summary>
    public static Error NotFound(Guid id) => Error.NotFound(NotFoundCode, "The job " + id.ToString("D") + " has no training from the simulation (yet).");
}
