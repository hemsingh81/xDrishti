using System.Globalization;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// How the queued unlock of the hold-out is written in <c>lab.lab_runs</c> (spec CF-06, part C). The job is a row of method <c>holdout</c> that copies the strategy, window and universe of the full run it unlocks; the run and the owner who asked are in
/// the first label, because the table has no column for them (shortcut: a column and a migration are the follow-up if a second kind of job needs the link). The key is the hash of the run, so one run has at most one open job, whoever races. The
/// password is verified at the endpoint and is never part of the job.
/// </summary>
internal static class HoldOutJob
{
    public const string Method = "holdout";

    public const string Stage = "holdout";

    private const string Prefix = "holdout-job run=";
    private const string ActorPart = " actor=";

    public static bool Is(LabRun run) => string.Equals(run.Method, Method, StringComparison.Ordinal);

    /// <summary>The mode a job's look is recorded with: <c>auto</c> for an actor that is a system actor (<c>system:auto-unlock</c>), else <c>owner</c>. The password check of the owner act happens at the endpoint, so a system actor is only ever written by the seal step.</summary>
    public static string ModeOf(string actor) => actor.StartsWith("system:", StringComparison.Ordinal) ? HoldOutUnlocker.AutoMode : HoldOutUnlocker.OwnerMode;

    /// <summary>The key of the queued look: the run AND the block (HO-01b), so a run that looked at block 2 can be queued again at block 3.</summary>
    public static string KeyOf(Guid runId, DateOnly blockStart) => LabRunIdentity.Sha256(string.Create(CultureInfo.InvariantCulture, $"holdout|{runId:D}|{blockStart:yyyy-MM-dd}"));

    public static string ConfigHash { get; } = LabRunIdentity.Sha256("holdout");

    public static string Label(Guid runId, string actor) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{runId:D}{ActorPart}{actor}");

    /// <summary>The run the job unlocks and the owner who asked, or null when the first label is not a job label.</summary>
    public static (Guid RunId, string Actor)? Parse(LabRun run)
    {
        var label = run.Labels.Count > 0 ? run.Labels[0] : null;
        if (label is null || !label.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = label[Prefix.Length..];
        var cut = body.IndexOf(ActorPart, StringComparison.Ordinal);
        return cut > 0 && Guid.TryParseExact(body[..cut], "D", out var id) && cut + ActorPart.Length < body.Length ? (id, body[(cut + ActorPart.Length)..]) : null;
    }
}
