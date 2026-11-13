using System.ComponentModel.DataAnnotations;
using XDrishti.Application.Abstractions.OpeningBars;

namespace XDrishti.Feed;

/// <summary>
/// The numbers of the opening-minute recorder (<c>OpeningBars:*</c>, BK-03b, documented in doc 16), bounds-checked at start. <see cref="Enabled"/> is the deployment gate and is <b>false</b> by default: with it false the recorder never reads credentials, opens a
/// socket or writes a row (it takes a restart to change). The owner's stored switch <c>baskets.opening_bars</c> and the paused service are the other two gates. The free-space floor is the options recorders' (<c>Options:MinFreeGb</c>, shared).
/// </summary>
internal sealed class OpeningBarsOptions : IValidatableObject
{
    public const string SectionName = "OpeningBars";

    /// <summary>The latest enabled formation time is 09:30 (basket 12 with its default 15 minutes) plus one minute.</summary>
    private static readonly TimeOnly EarliestRecordUntil = new(9, 31);

    /// <summary>The deployment gate (<c>OpeningBars__Enabled</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>The most shares subscribed on the recorder's own connection; the most liquid by ADV20 are kept.</summary>
    [Range(50, 5000)]
    public int MaxInstruments { get; set; } = 500;

    /// <summary>When the recorder closes (IST); at least 09:31.</summary>
    public TimeOnly RecordUntil { get; set; } = EarliestRecordUntil;

    /// <summary>How long after a minute ends its bar is written when no later packet comes.</summary>
    [Range(1, 30)]
    public int GraceSeconds { get; set; } = 5;

    /// <summary>How long after its formation time a basket may still be formed; later it is not formed (<c>late</c>).</summary>
    [Range(60, 600)]
    public int FormBySeconds { get; set; } = 120;

    /// <summary>Days of live rows kept (the owner's retention job, not the runtime role).</summary>
    [Range(1, 3650)]
    public int KeepDays { get; set; } = 60;

    /// <summary>How long without a packet on the whole connection counts as a feed gap.</summary>
    [Range(5, 300)]
    public int SilenceSeconds { get; set; } = 30;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RecordUntil < EarliestRecordUntil)
        {
            yield return new ValidationResult("RecordUntil must be 09:31 or later.", [nameof(RecordUntil)]);
        }
    }

    /// <summary>The settings the recorder works with; the free floor is the shared <c>Options:MinFreeGb</c>.</summary>
    public OpeningBarsSettings ToSettings(int minFreeGb) =>
        new(Enabled, MaxInstruments, RecordUntil, TimeSpan.FromSeconds(GraceSeconds), TimeSpan.FromSeconds(FormBySeconds), KeepDays, TimeSpan.FromSeconds(SilenceSeconds), minFreeGb * 1024L * 1024 * 1024);
}
