using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Application.SimAll;

/// <summary>The measured estimate (spec AU-12 non-functional): taken from the finished chunks once at least 1 % of the job (and one chunk) is done. An estimate, not a promise: throughput and storage per chunk were unverified until measured.</summary>
public sealed record SimAllEstimate(int SampledChunks, long AverageChunkMilliseconds, long RemainingSeconds, long AverageStoredBytes, long RemainingStoredBytes, string Basis);

/// <summary>Progress of the job, read by the report view while it runs (AU-14, AU-15).</summary>
public sealed record SimAllView(
    Guid Id,
    string State,
    string? PauseReason,
    DateOnly WindowFrom,
    DateOnly WindowTo,
    DateOnly HoldOutStart,
    DateOnly? CoveredFrom,
    DateOnly? CoveredTo,
    int ChunksTotal,
    int ChunksDone,
    int ChunksFailed,
    int ChunksRunning,
    int ChunksRemaining,
    int Workers,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,
    SimAllEstimate? Estimate)
{
    /// <summary>The bytes one stored trade is taken to add on top of the record text (rows, legs, indexes): an assumption, named in the estimate's basis.</summary>
    public const long BytesPerTrade = 400;

    /// <summary>Builds the view from a snapshot.</summary>
    public static SimAllView Of(SimAllSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (job, c) = (snapshot.Job, snapshot.Counts);
        var finished = c.Done + c.Failed;
        SimAllEstimate? estimate = null;
        if (c.Done >= 1 && finished * 100L >= c.Total)
        {
            var avgMs = c.SumMillis / c.Done;
            var avgBytes = (c.SumBytes + c.SumTrades * BytesPerTrade) / c.Done;
            estimate = new SimAllEstimate(c.Done, avgMs, avgMs * c.Remaining / Math.Max(1, job.Workers) / 1000, avgBytes, avgBytes * c.Remaining,
                $"mean of {c.Done} finished chunk(s); storage is the record text plus {BytesPerTrade} bytes per trade (an assumption); both change as more chunks finish");
        }

        return new SimAllView(job.Id, job.State, job.PauseReason, job.WindowFrom, job.WindowTo, job.HoldOutStart,
            c.Done > 0 ? job.WindowFrom : null, c.Done > 0 ? job.WindowTo : null,
            c.Total, c.Done, c.Failed, c.Running, c.Remaining, job.Workers, job.StartedBy, job.CreatedAt, job.UpdatedAt, job.FinishedAt, estimate);
    }
}

/// <summary>The commands of the job. The actor is the signed-in owner (<c>owner:&lt;e-mail&gt;</c>).</summary>
public sealed record StartSimAllCommand(string Actor) : Abstractions.Messaging.ICommand;

/// <summary>Pauses the active job.</summary>
public sealed record PauseSimAllCommand(string Actor) : Abstractions.Messaging.ICommand;

/// <summary>Resumes the paused job.</summary>
public sealed record ResumeSimAllCommand(string Actor) : Abstractions.Messaging.ICommand;

/// <summary>Cancels the active job.</summary>
public sealed record CancelSimAllCommand(string Actor) : Abstractions.Messaging.ICommand;

/// <summary>Reads the current (or newest) job.</summary>
public sealed record GetSimAllQuery : Abstractions.Messaging.IQuery<Domain.Common.Result<SimAllView>>;
