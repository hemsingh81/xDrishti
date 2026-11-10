using System.ComponentModel.DataAnnotations;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.SimTraining;

/// <summary>
/// The configuration section <c>Training</c> (spec AU-13, ADR 0249). Off by default: nothing trains from the simulation until the owner turns <see cref="Enabled"/> on. A value out of range stops start-up. The embargo default of 5 sessions equals the 7 calendar days of ADR 0067;
/// the rows are bounded because training runs inside xd-api, whose container limit is 1 GiB (deploy/compose.yaml), and a row costs about 2 KiB measured (150,000 rows is about 300 MiB, leaving room for the API itself; the range stops at 200,000 unless the container's memory budget is raised): above <see cref="MaxRows"/> the rows are thinned by a stable hash of the trade key (same inputs, same rows) and the summary says how much.
/// </summary>
public sealed class SimTrainingSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Training";

    /// <summary>Whether a finished simulate-everything job is trained on. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Sessions dropped from the end of the train rows before each test window, on top of the purge by label horizon (0 to 20).</summary>
    [Range(0, SimTrainingRules.MaxEmbargoSessions)]
    public int EmbargoSessions { get; set; } = SimTrainingRules.DefaultEmbargoSessions;

    /// <summary>Fewer usable closed trades than this: the answer is NOT_EVALUABLE, not a weak model.</summary>
    [Range(100, 1_000_000)]
    public int MinRows { get; set; } = 1000;

    /// <summary>The most rows held in memory at once; above it the rows are thinned deterministically.</summary>
    [Range(10_000, 200_000)]
    public int MaxRows { get; set; } = 150_000;

    /// <summary>Kept in the summary and the input hash so a changed setting is a new training.</summary>
    internal string KeyText() => $"embargo={EmbargoSessions};min_rows={MinRows};max_rows={MaxRows}";
}
