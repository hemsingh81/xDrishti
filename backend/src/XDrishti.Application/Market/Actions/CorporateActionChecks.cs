using XDrishti.Application.Market.Calendar;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>A checked corporate action (date, kind, ratio and note).</summary>
internal sealed record ActionInput(DateOnly ExDate, CorporateActionKind Kind, int Numerator, int Denominator, string? Note);

/// <summary>Checks shared by the corporate action use cases.</summary>
internal static class CorporateActionChecks
{
    /// <summary>Times a change is tried when the database refuses it because of a concurrent change (it reads the rows again each time).</summary>
    public const int Attempts = 3;

    /// <summary>How far ahead an ex-date may lie (announced actions).</summary>
    public const int MaxYearsAhead = 1;

    /// <summary>The most data lines one import reads.</summary>
    public const int MaxImportLines = 5_000;

    /// <summary>The most characters of one imported text.</summary>
    public const int MaxImportCharacters = 1_000_000;

    /// <summary>The first problems of an import that are listed.</summary>
    public const int MaxListedErrors = 50;

    /// <summary>The most unknown symbols an import lists.</summary>
    public const int MaxListedSymbols = 200;

    /// <summary>
    /// Checks a request. Fields left out (null) take the value of <paramref name="current"/> when there is one (an edit), else they are errors. A note left out keeps the current note; an empty one clears it.
    /// </summary>
    public static Result<ActionInput> Check(string? exDate, string? kind, int? numerator, int? denominator, string? note, ActionInput? current, DateOnly today)
    {
        var errors = new FieldErrors();

        var date = current?.ExDate ?? default;
        if (exDate is not null || current is null)
        {
            if (!CalendarChecks.TryDate(exDate, out date))
            {
                errors.Add("exDate", "Use the date as yyyy-MM-dd.");
            }
            else if (date < CorporateAction.MinDate || date > today.AddYears(MaxYearsAhead))
            {
                errors.Add("exDate", $"Use a date from {CorporateAction.MinDate:yyyy-MM-dd} to {MaxYearsAhead} year ahead.");
            }
        }

        var actionKind = current?.Kind ?? default;
        var haveKind = current is not null && kind is null;
        if (kind is not null || current is null)
        {
            haveKind = CorporateActionKindCodes.TryParse(kind?.Trim().ToLowerInvariant(), out actionKind);
            if (!haveKind)
            {
                errors.Add("kind", "Use split or bonus.");
            }
        }

        var top = current?.Numerator ?? 0;
        var haveTop = current is not null && numerator is null;
        if (numerator is not null || current is null)
        {
            haveTop = numerator is >= 1 and <= CorporateActionRatio.MaxPart;
            top = numerator ?? 0;
            if (!haveTop)
            {
                errors.Add("numerator", $"Use a whole number from 1 to {CorporateActionRatio.MaxPart}.");
            }
        }

        var bottom = current?.Denominator ?? 0;
        var haveBottom = current is not null && denominator is null;
        if (denominator is not null || current is null)
        {
            haveBottom = denominator is >= 1 and <= CorporateActionRatio.MaxPart;
            bottom = denominator ?? 0;
            if (!haveBottom)
            {
                errors.Add("denominator", $"Use a whole number from 1 to {CorporateActionRatio.MaxPart}.");
            }
        }

        if (haveKind && haveTop && haveBottom && !CorporateActionRatio.IsValid(actionKind, top, bottom))
        {
            errors.Add("denominator", "A split 1:1 changes nothing.");
        }

        var text = note is null ? current?.Note : note.Trim();
        if (text is { Length: > CorporateAction.MaxNoteLength })
        {
            errors.Add("note", $"Use at most {CorporateAction.MaxNoteLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("note", "Control characters are not allowed.");
        }

        return errors.HasErrors
            ? Result.Failure<ActionInput>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new ActionInput(date, actionKind, top, bottom, string.IsNullOrEmpty(text) ? null : text));
    }

    /// <summary>The audit text of an action: kind, ratio, symbol, ex-date and source — never the note.</summary>
    public static string Describe(CorporateAction action, string symbol) =>
        $"{(action.Kind == CorporateActionKind.Split ? "Split" : "Bonus")} {action.Ratio} {symbol} ex {action.ExDate:yyyy-MM-dd} ({action.Source.ToCode()})";
}
