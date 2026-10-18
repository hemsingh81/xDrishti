using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// Text in, <see cref="CostSheet"/> out (ADR 0042, D4). Strict and fail closed: a missing line, a JSON <c>null</c>, a number given as a string, a negative number, an unknown field, a property name repeated in one object, a rate with more than six
/// decimals, a date that is not <c>yyyy-MM-dd</c> and every other problem is a field error with a path (<c>schedules[1].delivery_equity.stt_sell_pct</c>) and a code, listed together up to
/// <see cref="CostErrors.MaxFieldErrors"/>. A null rate never reaches the model. The text is at most <see cref="CostErrors.MaxInputLength"/> characters.
/// </summary>
public static class CostSheetJson
{
    private const int MaxNameShown = 80;

    private static readonly string[] SheetFields = ["id", "label", "source", "checked", "schedules"];

    private static readonly string[] ScheduleFields = ["effective_from", "gst_pct", "rounding", "intraday_equity", "delivery_equity"];

    /// <summary>Reads the sheet. <c>costs.sheet_too_long</c> for a text over the bound, <c>costs.sheet_invalid</c> with the fields otherwise.</summary>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    public static Result<CostSheet> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > CostErrors.MaxInputLength)
        {
            return Result.Failure<CostSheet>(CostErrors.SheetTooLong("text", text.Length, CostErrors.MaxInputLength));
        }

        var root = MiniJson.Parse(text, out var problem);
        if (root is null)
        {
            return Result.Failure<CostSheet>(CostErrors.SheetInvalid(string.Empty, CostErrors.InvalidJson, "The text is not JSON: " + problem + "."));
        }

        var errors = new List<FieldError>();
        if (!Members(root, string.Empty, SheetFields, errors, out var sheet))
        {
            return Failure(errors);
        }

        var id = String(sheet, "id", string.Empty, errors, required: true);
        var label = String(sheet, "label", string.Empty, errors, required: true);
        var source = String(sheet, "source", string.Empty, errors, required: true);
        var checkedNote = String(sheet, "checked", string.Empty, errors, required: false);
        var schedules = new List<ChargeSchedule>();
        if (!sheet.TryGetValue("schedules", out var list) || list.Kind == MiniJson.Kind.Null)
        {
            errors.Add(new FieldError("schedules", "is required.", null, CostErrors.Required));
        }
        else if (list.Kind != MiniJson.Kind.Array)
        {
            errors.Add(new FieldError("schedules", "must be a list of schedules.", null, CostErrors.InvalidValue));
        }
        else if (list.Items.Count is 0 or > CostSheet.MaxSchedules)
        {
            errors.Add(list.Items.Count == 0
                ? new FieldError("schedules", "needs at least one schedule.", null, CostErrors.Required)
                : new FieldError("schedules", "has more than " + CostSheet.MaxSchedules.ToString(CultureInfo.InvariantCulture) + " schedules.", null, CostErrors.TooLarge));
        }
        else
        {
            for (var i = 0; i < list.Items.Count; i++)
            {
                if (Schedule(list.Items[i], "schedules[" + i.ToString(CultureInfo.InvariantCulture) + "]", errors) is { } schedule)
                {
                    schedules.Add(schedule);
                }
            }
        }

        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        var built = CostSheet.Create(id!, label!, source!, checkedNote, schedules);
        return built.IsFailure ? Result.Failure<CostSheet>(Prefixed(built.Error, string.Empty)) : built;
    }

    /// <summary>The digits of the mantissa of a number token (before any exponent) hold a digit other than 0.</summary>
    private static bool HasNonZeroDigit(string token)
    {
        foreach (var c in token)
        {
            if (c is 'e' or 'E')
            {
                return false;
            }

            if (c is >= '1' and <= '9')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A name from the caller as it may appear in a path: at most 80 characters, control characters shown as <c>\uXXXX</c>.</summary>
    private static string Safe(string name)
    {
        var shown = name.Length > MaxNameShown ? name[..MaxNameShown] + "..." : name;
        if (!shown.Any(char.IsControl))
        {
            return shown;
        }

        return string.Concat(shown.Select(c => char.IsControl(c) ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : c.ToString()));
    }

    private static Result<CostSheet> Failure(List<FieldError> errors) => Result.Failure<CostSheet>(CostErrors.SheetInvalid(errors));

    private static Error Prefixed(Error error, string prefix) =>
        prefix.Length == 0 ? error : error with { Fields = [.. (error.Fields ?? []).Select(f => f with { Field = prefix + "." + f.Field })] };

    private static ChargeSchedule? Schedule(MiniJson.Node node, string path, List<FieldError> errors)
    {
        if (!Members(node, path, ScheduleFields, errors, out var fields))
        {
            return null;
        }

        var before = errors.Count;
        var from = Date(fields, "effective_from", path, errors);
        var gst = Number(fields, "gst_pct", path, errors);
        var roundingName = String(fields, "rounding", path, errors, required: true);
        ChargeRounding? rounding = null;
        if (roundingName is not null)
        {
            rounding = CostSheet.RoundingOf(roundingName);
            if (rounding is null)
            {
                errors.Add(new FieldError(path + ".rounding", "must be half_away_from_zero, half_even or down.", null, CostErrors.InvalidValue));
            }
        }

        var intraday = Rates(fields, "intraday_equity", path, errors);
        var delivery = Rates(fields, "delivery_equity", path, errors);
        if (errors.Count > before || from is null || gst is null || rounding is null || intraday is null || delivery is null)
        {
            return null;
        }

        var built = ChargeSchedule.Create(from.Value, gst.Value, rounding.Value, intraday, delivery);
        if (built.IsFailure)
        {
            errors.AddRange(Prefixed(built.Error, path).Fields ?? []);
            return null;
        }

        return built.Value;
    }

    private static ChargeRates? Rates(Dictionary<string, MiniJson.Node> parent, string name, string parentPath, List<FieldError> errors)
    {
        var path = parentPath + "." + name;
        if (!parent.TryGetValue(name, out var node) || node.Kind == MiniJson.Kind.Null)
        {
            errors.Add(new FieldError(path, "is required.", null, CostErrors.Required));
            return null;
        }

        if (!Members(node, path, ChargeRates.FieldNames, errors, out var fields))
        {
            return null;
        }

        var before = errors.Count;
        var values = new decimal?[ChargeRates.FieldNames.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Number(fields, ChargeRates.FieldNames[i], path, errors);
        }

        if (errors.Count > before)
        {
            return null;
        }

        var built = ChargeRates.Create(values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value, values[4]!.Value, values[5]!.Value, values[6]!.Value, values[7]!.Value, values[8]!.Value, values[9]!.Value);
        if (built.IsFailure)
        {
            errors.AddRange(Prefixed(built.Error, path).Fields ?? []);
            return null;
        }

        return built.Value;
    }

    /// <summary>
    /// Checks that the node is an object and returns its members by name (the first of a repeated name); a name that is not allowed and a repeated name are field errors. Every member is judged, so that all problems are listed
    /// together; false only when the node is not an object.
    /// </summary>
    private static bool Members(MiniJson.Node node, string path, IReadOnlyCollection<string> allowed, List<FieldError> errors, out Dictionary<string, MiniJson.Node> fields)
    {
        fields = new Dictionary<string, MiniJson.Node>(StringComparer.Ordinal);
        if (node.Kind != MiniJson.Kind.Object)
        {
            errors.Add(new FieldError(path, "must be an object.", null, CostErrors.InvalidValue));
            return false;
        }

        foreach (var (name, value) in node.Members)
        {
            var memberPath = path.Length == 0 ? Safe(name) : path + "." + Safe(name);
            if (!allowed.Contains(name))
            {
                errors.Add(new FieldError(memberPath, "is not a field of the sheet.", null, CostErrors.UnknownField));
            }
            else if (!fields.TryAdd(name, value))
            {
                errors.Add(new FieldError(memberPath, "appears more than once in one object.", null, CostErrors.DuplicateProperty));
            }
        }

        return true;
    }

    private static string? String(Dictionary<string, MiniJson.Node> parent, string name, string parentPath, List<FieldError> errors, bool required)
    {
        var path = parentPath.Length == 0 ? name : parentPath + "." + name;
        if (!parent.TryGetValue(name, out var node) || node.Kind == MiniJson.Kind.Null)
        {
            if (required)
            {
                errors.Add(new FieldError(path, "is required.", null, CostErrors.Required));
            }

            return null;
        }

        if (node.Kind != MiniJson.Kind.String)
        {
            errors.Add(new FieldError(path, "must be text.", null, CostErrors.InvalidValue));
            return null;
        }

        return node.Text;
    }

    private static decimal? Number(Dictionary<string, MiniJson.Node> parent, string name, string parentPath, List<FieldError> errors)
    {
        var path = parentPath + "." + name;
        if (!parent.TryGetValue(name, out var node) || node.Kind == MiniJson.Kind.Null)
        {
            errors.Add(new FieldError(path, "is required (write a zero as 0, never leave it out or null).", null, CostErrors.Required));
            return null;
        }

        if (node.Kind == MiniJson.Kind.Number && decimal.TryParse(node.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            if (value == 0 && HasNonZeroDigit(node.Text))
            {
                // an underflow ("1e-29" or a literal with 29 decimals) reads as 0: that is a rate with too many decimals, never a zero
                errors.Add(new FieldError(path, "must have at most " + CostField.MaxScale.ToString(CultureInfo.InvariantCulture) + " decimal places.", null, CostErrors.TooPrecise));
                return null;
            }

            return value;
        }

        errors.Add(new FieldError(path, "must be a finite number written as a JSON number, not text.", null, CostErrors.NotANumber));
        return null;
    }

    private static DateOnly? Date(Dictionary<string, MiniJson.Node> parent, string name, string parentPath, List<FieldError> errors)
    {
        var text = String(parent, name, parentPath, errors, required: true);
        if (text is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(new FieldError(parentPath + "." + name, "must be a date written yyyy-MM-dd.", null, CostErrors.InvalidValue));
        return null;
    }
}
