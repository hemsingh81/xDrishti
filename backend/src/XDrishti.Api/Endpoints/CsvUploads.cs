using Microsoft.AspNetCore.Http.Features;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>A CSV upload as read from a multipart request: the other form fields, the account, the file's name and bytes and the field errors found so far (the caller adds its own before deciding).</summary>
internal sealed record CsvUpload(IFormCollection Form, Guid AccountId, string FileName, byte[] Bytes, FieldErrors Errors);

/// <summary>
/// Reads the multipart request of a file import (tradebook or dividend list): fields <c>accountId</c> and <c>file</c> and whatever else the import takes. The file is read into memory (at most 10 MB; the form options keep a file of that size out of the
/// temporary folder, so nothing is written to disk) and parsed by the use case; only its hash and name are ever stored. Anything wrong is <c>400 portfolio.invalid</c> with the field.
/// </summary>
internal static class CsvUploads
{
    public const int MaxBytes = DividendCsvLimits.MaxBytes;

    /// <summary>
    /// The longest text field read from a form: the dividend import's choices (a JSON array of at most <see cref="DividendCsvLimits.MaxChoices"/> choices; a choice is a name of up to 200 characters, a GUID, flags and punctuation,
    /// budgeted at <see cref="BytesPerChoice"/> characters). Every other text field is far shorter.
    /// </summary>
    public const int MaxFieldBytes = DividendCsvLimits.MaxChoices * BytesPerChoice;

    /// <summary>The characters a choice may take in the choices field (see <see cref="MaxFieldBytes"/>).</summary>
    public const int BytesPerChoice = 768;

    /// <summary>
    /// The form entries read: <c>accountId</c>, <c>digest</c>, <c>choices</c>, <c>rebuild</c>, the file and a few more, and <c>includeDuplicates</c>, which may be sent once per line (at most
    /// <see cref="DividendCsvLimits.MaxRows"/> of them; the field is capped there too).
    /// </summary>
    public const int MaxFormEntries = DividendCsvLimits.MaxRows + 32;

    /// <summary>The form carries the file and a few short fields (and, for the dividend import, a list of choices); the headroom covers the multipart framing.</summary>
    public const int MaxRequestBytes = MaxBytes + MaxFieldBytes + (512 * 1024);

    /// <param name="http">The request.</param>
    /// <param name="fileLabel">What the file is called in messages (<c>the tradebook</c>, <c>the dividend list</c>).</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<Result<CsvUpload>> ReadAsync(HttpContext http, string fileLabel, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(http, MaxRequestBytes, "accountId and file", $"The file is larger than {MaxBytes / (1024 * 1024)} MB.", cancellationToken);
        if (form.IsFailure)
        {
            return Result.Failure<CsvUpload>(form.Error);
        }

        var errors = new FieldErrors();
        if (!Guid.TryParse(form.Value["accountId"].ToString(), out var accountId) || accountId == Guid.Empty)
        {
            errors.Add("accountId", "Required: the id of an account with the Portfolio role.");
        }

        var file = form.Value.Files.GetFile("file");
        byte[] bytes = [];
        if (file is null)
        {
            errors.Add("file", $"Required: {fileLabel} as a CSV file.");
        }
        else if (file.Length == 0)
        {
            errors.Add("file", "The file is empty.");
        }
        else if (file.Length > MaxBytes)
        {
            errors.Add("file", $"The file is larger than {MaxBytes / (1024 * 1024)} MB.");
        }
        else
        {
            bytes = new byte[file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        return Result.Success(new CsvUpload(form.Value, accountId, file?.FileName ?? string.Empty, bytes, errors));
    }

    /// <summary>
    /// Reads the multipart form of a file import with the form limits set for this request only (not app-wide): files stay in memory, never in the temporary folder, and there are at most a few fields beside them. The request as a whole is at most
    /// <paramref name="maxRequestBytes"/> long. A request that is not multipart (<paramref name="fields"/> names what it should carry), is too large (<paramref name="tooLarge"/> says so) or cannot be read is <c>400 portfolio.invalid</c> on <c>file</c>.
    /// </summary>
    public static async Task<Result<IFormCollection>> ReadFormAsync(HttpContext http, int maxRequestBytes, string fields, string tooLarge, CancellationToken cancellationToken)
    {
        if (!http.Request.HasFormContentType)
        {
            return FailForm("file", $"Send multipart/form-data with the fields {fields}.");
        }

        if (http.Request.ContentLength > maxRequestBytes)
        {
            return FailForm("file", tooLarge);
        }

        var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = maxRequestBytes;
        }

        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
        {
            MultipartBodyLengthLimit = maxRequestBytes,
            MemoryBufferThreshold = maxRequestBytes,
            ValueCountLimit = MaxFormEntries,
            ValueLengthLimit = MaxFieldBytes,
            MultipartHeadersCountLimit = 16 + (4 * ReportUploads.MaxFiles),
        }));
        try
        {
            return Result.Success(await http.Request.ReadFormAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            var failed = Overflow(ex);
            return Result.Failure<IFormCollection>(failed.Error);
        }
    }

    /// <summary>What the form's limits refuse, with the field to blame: too many entries are <c>includeDuplicates</c> sent line by line, anything else the file. (A multipart text field is not cut by the form options, only the request as a whole is limited; the size of the choices is checked by <see cref="DividendUploads"/>.)</summary>
    private static Result<CsvUpload> Overflow(Exception ex) => ex.Message switch
    {
        var m when m.Contains("value count", StringComparison.OrdinalIgnoreCase) => Fail(
            "includeDuplicates", $"The form has more than {MaxFormEntries:N0} entries. Send includeDuplicates as one JSON array of line numbers instead of one field per line."),
        _ => Fail("file", $"The upload cannot be read or is larger than {MaxBytes / (1024 * 1024)} MB."),
    };

    private static Result<CsvUpload> Fail(string field, string message) => Result.Failure<CsvUpload>(PortfolioErrors.Invalid(field, message));

    private static Result<IFormCollection> FailForm(string field, string message) => Result.Failure<IFormCollection>(PortfolioErrors.Invalid(field, message));
}
