using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Actions;

/// <summary>
/// A split or bonus of one instrument on its ex-date (ADR 0017 §5, <c>market.corporate_actions</c>). Unique per instrument, ex-date and kind. Bars stay raw; an
/// <see cref="CorporateActionStatus.Applied"/> action makes adjusted reads multiply the bars <em>before</em> the ex-date by <see cref="Factor"/> (see <see cref="CorporateActionRatio"/> for the
/// meaning of the ratio). Rows are never deleted: a declined action is <see cref="CorporateActionStatus.Ignored"/>.
/// </summary>
public sealed class CorporateAction : Entity<Guid>
{
    public const int MaxNoteLength = 200;

    /// <summary>The earliest ex-date accepted.</summary>
    public static readonly DateOnly MinDate = new(2000, 1, 1);

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private CorporateAction(
        Guid id,
        Guid instrumentId,
        DateOnly exDate,
        CorporateActionKind kind,
        int numerator,
        int denominator,
        decimal factor,
        CorporateActionStatus status,
        CorporateActionSource source,
        string? note,
        DateTimeOffset createdAt,
        DateTimeOffset? decidedAt,
        string? decidedBy,
        DateOnly? detectedExDate)
        : base(id)
    {
        InstrumentId = instrumentId;
        ExDate = exDate;
        Kind = kind;
        Numerator = numerator;
        Denominator = denominator;
        Factor = factor;
        Status = status;
        Source = source;
        Note = note;
        CreatedAt = createdAt;
        DecidedAt = decidedAt;
        DecidedBy = decidedBy;
        DetectedExDate = detectedExDate;
    }

    public Guid InstrumentId { get; private set; }

    public DateOnly ExDate { get; private set; }

    public CorporateActionKind Kind { get; private set; }

    public int Numerator { get; private set; }

    public int Denominator { get; private set; }

    /// <summary>The price multiplier for bars before the ex-date (derived from the ratio and stored).</summary>
    public decimal Factor { get; private set; }

    public CorporateActionStatus Status { get; private set; }

    public CorporateActionSource Source { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the owner confirmed, edited or ignored it (null while it is only suggested).</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    public string? DecidedBy { get; private set; }

    /// <summary>
    /// The ex-date the detection first found this action on (null for an action nobody detected). It stays when the owner corrects the ex-date, so the detection does not suggest the date it found
    /// again once the action has moved off it.
    /// </summary>
    public DateOnly? DetectedExDate { get; private set; }

    public string Ratio => CorporateActionRatio.Text(Numerator, Denominator);

    /// <summary>An action the owner (or an imported list) states: applied at once.</summary>
    public static CorporateAction Apply(Guid instrumentId, DateOnly exDate, CorporateActionKind kind, int numerator, int denominator, CorporateActionSource source, string? note, string actor, DateTimeOffset now)
    {
        Check(exDate, kind, numerator, denominator);
        return new CorporateAction(Guid.CreateVersion7(now), instrumentId, exDate, kind, numerator, denominator, CorporateActionRatio.Factor(kind, numerator, denominator), CorporateActionStatus.Applied, source, Clean(note), now, now, Actor(actor), null);
    }

    /// <summary>A candidate the detection found: changes nothing until the owner decides.</summary>
    public static CorporateAction Suggest(Guid instrumentId, DateOnly exDate, CorporateActionKind kind, int numerator, int denominator, string? note, DateTimeOffset now)
    {
        Check(exDate, kind, numerator, denominator);
        return new CorporateAction(Guid.CreateVersion7(now), instrumentId, exDate, kind, numerator, denominator, CorporateActionRatio.Factor(kind, numerator, denominator), CorporateActionStatus.Suggested, CorporateActionSource.Detected, Clean(note), now, null, null, exDate);
    }

    /// <summary>
    /// Sets date, kind, ratio and note and makes the action applied (confirming a suggestion or bringing back an ignored one). Returns false when nothing changed (already applied with the same values).
    /// </summary>
    public bool Confirm(DateOnly exDate, CorporateActionKind kind, int numerator, int denominator, string? note, string actor, DateTimeOffset now)
    {
        Check(exDate, kind, numerator, denominator);
        var cleaned = Clean(note);
        if (Status == CorporateActionStatus.Applied && ExDate == exDate && Kind == kind && Numerator == numerator && Denominator == denominator && Note == cleaned)
        {
            return false;
        }

        ExDate = exDate;
        Kind = kind;
        Numerator = numerator;
        Denominator = denominator;
        Factor = CorporateActionRatio.Factor(kind, numerator, denominator);
        Note = cleaned;
        Status = CorporateActionStatus.Applied;
        DecidedAt = now;
        DecidedBy = Actor(actor);
        return true;
    }

    /// <summary>The owner declines it (a suggestion, or an applied action taken back). Returns false when it was ignored already.</summary>
    public bool Ignore(string actor, DateTimeOffset now)
    {
        if (Status == CorporateActionStatus.Ignored)
        {
            return false;
        }

        Status = CorporateActionStatus.Ignored;
        DecidedAt = now;
        DecidedBy = Actor(actor);
        return true;
    }

    /// <summary>Whether a date can be an ex-date at all.</summary>
    public static bool IsValidDate(DateOnly exDate) => exDate >= MinDate;

    private static void Check(DateOnly exDate, CorporateActionKind kind, int numerator, int denominator)
    {
        if (!IsValidDate(exDate))
        {
            throw new ArgumentOutOfRangeException(nameof(exDate), exDate, "The ex-date is before the data starts.");
        }

        if (!CorporateActionRatio.IsValid(kind, numerator, denominator))
        {
            throw new ArgumentException("The ratio is not a corporate action.");
        }
    }

    private static string Actor(string actor) => string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim().Length <= 256 ? actor.Trim() : actor.Trim()[..256];

    private static string? Clean(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= MaxNoteLength ? trimmed : trimmed[..MaxNoteLength];
    }
}
