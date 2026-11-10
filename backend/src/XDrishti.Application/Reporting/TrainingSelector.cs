namespace XDrishti.Application.Reporting;

/// <summary>A report over a simulate-everything job (spec AU-14, ADR 0243): a training simulation, never evidence. <paramref name="JobId"/> null means the active job, else the newest.</summary>
/// <param name="JobId">The job.</param>
public sealed record TrainingSelector(Guid? JobId);
