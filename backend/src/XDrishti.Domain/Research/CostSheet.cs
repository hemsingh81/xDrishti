using System.Globalization;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// A broker's charge sheet (ADR 0042, D4 to D7): an id, a label, where the figures came from, what they were checked against (absent means unchecked), and 1 to 12 effective-dated schedules. The rates in force on a date are those of the last
/// schedule that starts on or before it. An id that starts with <c>test-</c> is a TEST sheet (synthetic numbers; the stores of P3-03f refuse to keep one). The canonical text and its hashes do not depend on the spelling of the input.
/// Only <see cref="Create"/> and <see cref="CostSheetJson.Parse"/> build one; a rate that is missing makes a sheet impossible to build.
/// </summary>
public sealed record CostSheet : IEquatable<CostSheet>
{
    /// <summary>The prefix of the id of a synthetic sheet.</summary>
    public const string TestPrefix = "test-";

    /// <summary>The most schedules a sheet has.</summary>
    public const int MaxSchedules = 12;

    private const int MaxId = 40;

    private const int MaxLabel = 80;

    private const int MaxSource = 200;

    private const int MaxChecked = 200;

    private readonly ChargeSchedule[] _schedules;

    private readonly IReadOnlyList<ChargeSchedule> _view;

    private CostSheet(string id, string label, string source, string? checkedNote, ChargeSchedule[] schedules)
    {
        Id = id;
        Label = label;
        Source = source;
        Checked = checkedNote;
        _schedules = schedules;
        _view = Array.AsReadOnly(schedules);
    }

    /// <summary>Lower-case <c>[a-z0-9][a-z0-9._-]{0,39}</c>.</summary>
    public string Id { get; }

    /// <summary>A short name for people (at most 80 characters).</summary>
    public string Label { get; }

    /// <summary>Where the figures were read, and when (at most 200 characters).</summary>
    public string Source { get; }

    /// <summary>What the sheet was checked against; null means unchecked and every label of a run says so.</summary>
    public string? Checked { get; }

    /// <summary>Whether the id starts with <c>test-</c>: a synthetic sheet for tests, not a broker's.</summary>
    public bool IsTest => Id.StartsWith(TestPrefix, StringComparison.Ordinal);

    /// <summary>The schedules, strictly ascending by <see cref="ChargeSchedule.EffectiveFrom"/>.</summary>
    public IReadOnlyList<ChargeSchedule> Schedules => _view;

    /// <summary>The lower-case hex SHA-256 of <see cref="RatesJson"/>: the id and the schedules, not the label, the source or the note. Equal for equal rates.</summary>
    public string RatesSha256 => CanonicalText.Sha256Hex(RatesJson());

    /// <summary>
    /// Builds the sheet. A problem is a field error with a path (<c>schedules[1].effective_from</c>) and a code of <see cref="CostErrors"/>; all problems are listed, at most <see cref="CostErrors.MaxFieldErrors"/>.
    /// </summary>
    public static Result<CostSheet> Create(string id, string label, string source, string? checkedNote, IReadOnlyList<ChargeSchedule> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var errors = new List<FieldError>();
        if (id is null || !IsValidId(id))
        {
            errors.Add(new FieldError("id", "must be lower case letters, digits, '.', '_' or '-', start with a letter or digit and have at most " + MaxId + " characters.", null, CostErrors.InvalidValue));
        }

        CheckText(errors, "label", label, MaxLabel, required: true);
        CheckText(errors, "source", source, MaxSource, required: true);
        if (checkedNote is not null)
        {
            CheckText(errors, "checked", checkedNote, MaxChecked, required: true);
        }

        if (schedules.Count == 0)
        {
            errors.Add(new FieldError("schedules", "needs at least one schedule.", null, CostErrors.Required));
        }
        else if (schedules.Count > MaxSchedules)
        {
            errors.Add(new FieldError("schedules", "has more than " + MaxSchedules + " schedules.", null, CostErrors.TooLarge));
        }
        else
        {
            for (var i = 1; i < schedules.Count; i++)
            {
                if (schedules[i].EffectiveFrom == schedules[i - 1].EffectiveFrom)
                {
                    errors.Add(new FieldError("schedules[" + i.ToString(CultureInfo.InvariantCulture) + "].effective_from", "repeats the date of the schedule before it.", null, CostErrors.Duplicate));
                }
                else if (schedules[i].EffectiveFrom < schedules[i - 1].EffectiveFrom)
                {
                    errors.Add(new FieldError("schedules[" + i.ToString(CultureInfo.InvariantCulture) + "].effective_from", "must be later than the schedule before it.", null, CostErrors.NotAscending));
                }
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure<CostSheet>(CostErrors.SheetInvalid(errors));
        }

        var sheet = new CostSheet(id!, label, source, checkedNote, [.. schedules]);
        var length = sheet.ToCanonicalJson().Length;
        return length > CostErrors.MaxCanonicalLength
            ? Result.Failure<CostSheet>(CostErrors.SheetTooLong("canonical text", length, CostErrors.MaxCanonicalLength))
            : Result.Success(sheet);
    }

    /// <summary>The schedule in force on the date: the last one that starts on or before it; <c>costs.no_rates_for_date</c> before the first.</summary>
    public Result<ChargeSchedule> RatesOn(DateOnly date)
    {
        for (var i = _schedules.Length - 1; i >= 0; i--)
        {
            if (_schedules[i].EffectiveFrom <= date)
            {
                return Result.Success(_schedules[i]);
            }
        }

        return Result.Failure<ChargeSchedule>(CostErrors.NoRatesForDate(Id, date));
    }

    /// <summary>The canonical text: <c>id</c>, <c>label</c>, <c>source</c>, <c>checked</c> (only when present) and <c>schedules</c>, snake_case keys, no white space, numbers by <see cref="CanonicalText.Number(decimal)"/>, dates as <c>yyyy-MM-dd</c>.</summary>
    public string ToCanonicalJson() => Write(descriptive: true);

    /// <summary>The same text restricted to <c>id</c> and <c>schedules</c>: what <see cref="RatesSha256"/> hashes.</summary>
    public string RatesJson() => Write(descriptive: false);

    /// <summary>Value equality: every field, and the schedules in order.</summary>
    public bool Equals(CostSheet? other) =>
        other is not null && Id == other.Id && Label == other.Label && Source == other.Source && Checked == other.Checked && _schedules.AsSpan().SequenceEqual(other._schedules);

    /// <summary>A hash consistent with <see cref="Equals(CostSheet?)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(Id, Label, Source, Checked, _schedules.Length);

    /// <summary>The canonical name of a rounding rule.</summary>
    internal static string RoundingName(ChargeRounding rounding) => rounding switch
    {
        ChargeRounding.HalfAwayFromZero => "half_away_from_zero",
        ChargeRounding.HalfEven => "half_even",
        ChargeRounding.Down => "down",
        _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown rounding rule."),
    };

    /// <summary>The rounding rule of a canonical name, or null.</summary>
    internal static ChargeRounding? RoundingOf(string name) => name switch
    {
        "half_away_from_zero" => ChargeRounding.HalfAwayFromZero,
        "half_even" => ChargeRounding.HalfEven,
        "down" => ChargeRounding.Down,
        _ => null,
    };

    /// <summary>Whether the id matches <c>[a-z0-9][a-z0-9._-]{0,39}</c>.</summary>
    public static bool IsValidId(string id)
    {
        if (id.Length is 0 or > MaxId)
        {
            return false;
        }

        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            var alnum = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!(alnum || (i > 0 && c is '.' or '_' or '-')))
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckText(List<FieldError> errors, string path, string? value, int max, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add(new FieldError(path, "is required.", null, CostErrors.Required));
            }

            return;
        }

        if (value.Length > max)
        {
            errors.Add(new FieldError(path, "must have at most " + max + " characters.", null, CostErrors.TooLong));
        }
        else if (value.Any(char.IsControl))
        {
            errors.Add(new FieldError(path, "must not hold control characters.", null, CostErrors.InvalidValue));
        }
    }

    /// <summary>The canonical text without <c>checked</c>: what a run record keeps, so that writing the note after a successful check changes neither the record nor the run key.</summary>
    internal string CanonicalJsonWithoutChecked() => Write(descriptive: true, includeChecked: false);

    private string Write(bool descriptive, bool includeChecked = true)
    {
        var text = new StringBuilder();
        text.Append("{\"id\":");
        Quote(text, Id);
        if (descriptive)
        {
            text.Append(",\"label\":");
            Quote(text, Label);
            text.Append(",\"source\":");
            Quote(text, Source);
            if (includeChecked && Checked is not null)
            {
                text.Append(",\"checked\":");
                Quote(text, Checked);
            }
        }

        text.Append(",\"schedules\":[");
        for (var i = 0; i < _schedules.Length; i++)
        {
            var s = _schedules[i];
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append("{\"effective_from\":\"").Append(s.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\",\"gst_pct\":").Append(CanonicalText.Number(s.GstPct))
                .Append(",\"rounding\":\"").Append(RoundingName(s.Rounding)).Append("\",\"intraday_equity\":");
            Rates(text, s.IntradayEquity);
            text.Append(",\"delivery_equity\":");
            Rates(text, s.Delivery);
            text.Append('}');
        }

        text.Append("]}");
        return text.ToString();
    }

    private static void Rates(StringBuilder text, ChargeRates rates)
    {
        var names = ChargeRates.FieldNames;
        var values = rates.Values();
        text.Append('{');
        for (var i = 0; i < names.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append('"').Append(names[i]).Append("\":").Append(CanonicalText.Number(values[i]));
        }

        text.Append('}');
    }

    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                default:
                    if (c < ' ')
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        text.Append('"');
    }
}
