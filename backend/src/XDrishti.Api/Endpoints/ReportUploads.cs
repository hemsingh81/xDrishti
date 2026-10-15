using System.Text.Json;
using XDrishti.Api.Infrastructure;
using XDrishti.Application.Portfolio.Reports;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>A file of a report upload: its name and bytes, or — when it cannot be taken in — the reason (the other files are still handled).</summary>
internal sealed record ReportUploadFile(string FileName, TradeFileContent? Content, ImportFileError? Problem);

/// <summary>
/// A report upload as read from the multipart request (ADR 0029): the account, the files in the order they were sent and, for an import, one digest per file (null where none was sent), whether to rebuild the lots, the indexes of the files to import
/// although they are for another client code, and the owner's choices of what scrip names mean.
/// </summary>
internal sealed record ReportUpload(
    Guid AccountId, IReadOnlyList<ReportUploadFile> Files, IReadOnlyList<string?> Digests, bool Rebuild, IReadOnlySet<int> AcceptOtherAccount, IReadOnlyList<BillChoice> Choices);

/// <summary>
/// Reads the multipart request of the report preview and import: fields <c>accountId</c>, <c>files</c> (one or more, at most <see cref="MaxFiles"/> of at most 10 MB, 40 MB together) and, beside them, <c>choices</c> (a JSON array of choices), and for an import
/// <c>digests</c> (a JSON array with one digest or null per file, in order), <c>rebuild</c> (true | false, default true) and <c>acceptOtherAccount</c> (a JSON array of file indexes from 0). A single <c>file</c> is read like <c>files</c>. The files are held in memory and
/// parsed by the use case; only their hashes and names are ever stored. Anything wrong with the request is <c>400 portfolio.invalid</c> with the field; a file that is empty or too large is a problem of that file only.
/// </summary>
internal static class ReportUploads
{
    public const int MaxFiles = ReportImportLimits.MaxFiles;

    /// <summary>All the files of a request together.</summary>
    public const int MaxTotalBytes = 4 * TradeCsvLimits.MaxBytes;

    public const int MaxRequestBytes = MaxTotalBytes + CsvUploads.MaxFieldBytes + (512 * 1024);

    public static async Task<Result<ReportUpload>> ReadAsync(HttpContext http, bool forImport, CancellationToken cancellationToken)
    {
        var read = await CsvUploads.ReadFormAsync(http, MaxRequestBytes, "accountId and files", $"The upload is larger than {MaxTotalBytes / (1024 * 1024)} MB. Send fewer files at a time.", cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<ReportUpload>(read.Error);
        }

        var form = read.Value;
        var errors = new FieldErrors();
        if (!Guid.TryParse(form["accountId"].ToString(), out var accountId) || accountId == Guid.Empty)
        {
            errors.Add("accountId", "Required: the id of an account with the Portfolio role.");
        }

        var parts = form.Files.Where(f => f.Name is "files" or "file").ToList();
        if (parts.Count == 0)
        {
            errors.Add("files", "Required: one or more report files.");
        }
        else if (parts.Count > MaxFiles)
        {
            errors.Add("files", $"At most {MaxFiles} files at a time.");
            parts = [];
        }
        else if (parts.Sum(f => f.Length) > MaxTotalBytes)
        {
            errors.Add("files", $"The files together are larger than {MaxTotalBytes / (1024 * 1024)} MB. Send fewer at a time.");
            parts = [];
        }

        var choices = Choices(form["choices"].ToString(), errors);
        var digests = forImport ? Digests(form["digests"].ToString(), parts.Count, errors) : [];
        var rebuild = true;
        var rebuildText = form["rebuild"].ToString();
        if (forImport && rebuildText.Length > 0 && !bool.TryParse(rebuildText, out rebuild))
        {
            errors.Add("rebuild", "Use true or false.");
        }

        var accept = forImport ? Indexes(form["acceptOtherAccount"].ToString(), parts.Count, errors) : [];
        if (errors.HasErrors)
        {
            return Result.Failure<ReportUpload>(PortfolioErrors.Invalid(errors));
        }

        var files = new List<ReportUploadFile>(parts.Count);
        foreach (var part in parts)
        {
            files.Add(await TakeAsync(part, cancellationToken));
        }

        return Result.Success(new ReportUpload(accountId, files, digests, forImport && rebuild, accept, choices));
    }

    private static async Task<ReportUploadFile> TakeAsync(IFormFile part, CancellationToken cancellationToken)
    {
        if (part.Length == 0)
        {
            return new ReportUploadFile(part.FileName, null, new ImportFileError("portfolio.invalid", "The file is empty.", "file"));
        }

        if (part.Length > TradeCsvLimits.MaxBytes)
        {
            return new ReportUploadFile(part.FileName, null, new ImportFileError("portfolio.invalid", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB.", "file"));
        }

        var bytes = new byte[part.Length];
        await using var stream = part.OpenReadStream();
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return new ReportUploadFile(part.FileName, new TradeFileContent(bytes), null);
    }

    private static List<BillChoice> Choices(string text, FieldErrors errors)
    {
        // A multipart text field is not cut by the form options (only the request as a whole is limited), so the size of the choices is checked here.
        if (text.Length > CsvUploads.MaxFieldBytes)
        {
            errors.Add("choices", $"Longer than {CsvUploads.MaxFieldBytes:N0} characters: send at most {ReportImportLimits.MaxChoices:N0} choices.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var choices = JsonSerializer.Deserialize(text, ApiJsonContext.Default.BillChoiceArray);
            if (choices is not null && choices.All(c => c is not null))
            {
                return [.. choices];
            }
        }
        catch (JsonException)
        {
            // reported below
        }

        errors.Add("choices", "Send a JSON array of {name, holdingId | instrumentId | isin, remember}.");
        return [];
    }

    private static List<string?> Digests(string text, int files, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [.. Enumerable.Repeat<string?>(null, files)];
        }

        try
        {
            var digests = JsonSerializer.Deserialize(text, ApiJsonContext.Default.StringArray);
            if (digests is not null && digests.Length <= files)
            {
                return [.. digests.Select(d => string.IsNullOrWhiteSpace(d) ? null : d.Trim()).Concat(Enumerable.Repeat<string?>(null, files - digests.Length))];
            }
        }
        catch (JsonException)
        {
            // reported below
        }

        errors.Add("digests", "Send a JSON array with one digest (or null) for each file, in the order the files were sent.");
        return [];
    }

    private static HashSet<int> Indexes(string text, int files, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var indexes = JsonSerializer.Deserialize(text, ApiJsonContext.Default.Int32Array);
            if (indexes is not null && indexes.All(i => i >= 0 && i < Math.Max(files, 1)))
            {
                return [.. indexes];
            }
        }
        catch (JsonException)
        {
            // reported below
        }

        errors.Add("acceptOtherAccount", "Send a JSON array of the indexes (from 0) of the files to import although they are for another client code.");
        return [];
    }
}
