using System.Text.Json;
using XDrishti.Api.Infrastructure;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Reads the multipart request of the dividend preview and import: fields <c>accountId</c>, <c>file</c> (read by <see cref="CsvUploads"/>) and, for an import, <c>digest</c>, <c>choices</c> (a JSON array of choices) and <c>includeDuplicates</c> (a JSON array
/// of line numbers, or numbers separated by commas, or the field repeated). Anything wrong is <c>400 portfolio.invalid</c> with the field.
/// </summary>
internal static class DividendUploads
{
    public static async Task<Result<DividendUpload>> ReadAsync(HttpContext http, bool forImport, CancellationToken cancellationToken)
    {
        var read = await CsvUploads.ReadAsync(http, "the dividend list", cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<DividendUpload>(read.Error);
        }

        var upload = read.Value;
        var errors = upload.Errors;
        IReadOnlyList<DividendChoice> choices = [];
        IReadOnlyList<int> include = [];
        if (forImport)
        {
            choices = Choices(upload.Form["choices"].ToString(), errors);
            include = Lines(upload.Form["includeDuplicates"].ToArray(), errors);
        }

        if (errors.HasErrors)
        {
            return Result.Failure<DividendUpload>(PortfolioErrors.Invalid(errors));
        }

        var digest = upload.Form["digest"].ToString().Trim();
        return Result.Success(new DividendUpload(upload.AccountId, upload.FileName, new TradeFileContent(upload.Bytes), digest.Length == 0 ? null : digest, choices, include));
    }

    private static List<DividendChoice> Choices(string text, FieldErrors errors)
    {
        // A multipart text field is not cut by the form options (only the request as a whole is limited), so the size of the choices is checked here.
        if (text.Length > CsvUploads.MaxFieldBytes)
        {
            errors.Add("choices", $"Longer than {CsvUploads.MaxFieldBytes:N0} characters: send at most {DividendCsvLimits.MaxChoices:N0} choices.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var choices = JsonSerializer.Deserialize(text, ApiJsonContext.Default.DividendChoiceArray);
            if (choices is not null && choices.All(c => c is not null))
            {
                return [.. choices];
            }
        }
        catch (JsonException)
        {
            // reported below
        }

        errors.Add("choices", "Send a JSON array of {name, holdingId | instrumentId | none, remember}.");
        return [];
    }

    /// <summary>The most characters one <c>includeDuplicates</c> value may have: <see cref="DividendCsvLimits.MaxRows"/> line numbers of up to seven digits and a comma each.</summary>
    internal const int MaxLinesValueLength = DividendCsvLimits.MaxRows * 8;

    /// <summary>
    /// The line numbers of the <c>includeDuplicates</c> values. Each value is cut at <see cref="MaxLinesValueLength"/> characters and the reading stops at the first number over <see cref="DividendCsvLimits.MaxRows"/>, before the rest is parsed: the field is
    /// untrusted and is not cut by the form options.
    /// </summary>
    internal static List<int> Lines(string?[] values, FieldErrors errors)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(errors);
        var lines = new List<int>();
        foreach (var value in values.Select(v => v?.Trim()).Where(v => !string.IsNullOrEmpty(v)))
        {
            if (value!.Length > MaxLinesValueLength)
            {
                errors.Add("includeDuplicates", $"A value is longer than {MaxLinesValueLength:N0} characters: send at most {DividendCsvLimits.MaxRows:N0} line numbers.");
                return [];
            }

            try
            {
                if (value[0] == '[')
                {
                    lines.AddRange(JsonSerializer.Deserialize(value, ApiJsonContext.Default.Int32Array) ?? []);
                }
                else
                {
                    foreach (var range in value.AsSpan().Split(','))
                    {
                        var text = value.AsSpan(range).Trim();
                        if (text.IsEmpty)
                        {
                            continue;
                        }

                        lines.Add(int.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture));
                        if (lines.Count > DividendCsvLimits.MaxRows)
                        {
                            break; // over the limit: reported below, the rest is not parsed
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or FormatException or OverflowException)
            {
                errors.Add("includeDuplicates", "Send a JSON array of line numbers, or numbers separated by commas.");
                return [];
            }

            if (lines.Count > DividendCsvLimits.MaxRows)
            {
                break;
            }
        }

        if (lines.Count > DividendCsvLimits.MaxRows)
        {
            errors.Add("includeDuplicates", $"At most {DividendCsvLimits.MaxRows:N0} lines.");
            return [];
        }

        return lines;
    }
}
