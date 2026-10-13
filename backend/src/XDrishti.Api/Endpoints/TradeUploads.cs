using Microsoft.AspNetCore.Http.Features;
using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Reads the multipart request of the trade preview and import: fields <c>accountId</c>, <c>file</c> and, for an import, <c>rebuild</c> and <c>digest</c>. The file is read into memory (at most 10 MB; the form options keep a file of that size out of the
/// temporary folder, so nothing is written to disk) and parsed by the use case; only its hash and name are ever stored. Anything wrong is <c>400 portfolio.invalid</c> with the field.
/// </summary>
internal static class TradeUploads
{
    /// <summary>The form carries the file and a few short fields; the headroom covers the multipart framing.</summary>
    public const int MaxRequestBytes = TradeCsvLimits.MaxBytes + (256 * 1024);

    public static async Task<Result<TradeUpload>> ReadAsync(HttpContext http, bool forImport, CancellationToken cancellationToken)
    {
        if (!http.Request.HasFormContentType)
        {
            return Fail("file", "Send multipart/form-data with the fields accountId and file.");
        }

        if (http.Request.ContentLength > MaxRequestBytes)
        {
            return Fail("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB.");
        }

        var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = MaxRequestBytes;
        }

        // The form limits are set for these requests only (not app-wide): a file of up to 10 MB stays in memory, never in the temporary folder, and there are at most a few short fields beside it.
        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
        {
            MultipartBodyLengthLimit = MaxRequestBytes,
            MemoryBufferThreshold = MaxRequestBytes,
            ValueCountLimit = 16,
            MultipartHeadersCountLimit = 16,
        }));
        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            return Fail("file", $"The upload cannot be read or is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB.");
        }

        var errors = new FieldErrors();
        if (!Guid.TryParse(form["accountId"].ToString(), out var accountId) || accountId == Guid.Empty)
        {
            errors.Add("accountId", "Required: the id of an account with the Portfolio role.");
        }

        var file = form.Files.GetFile("file");
        byte[] bytes = [];
        if (file is null)
        {
            errors.Add("file", "Required: the tradebook as a CSV file.");
        }
        else if (file.Length == 0)
        {
            errors.Add("file", "The file is empty.");
        }
        else if (file.Length > TradeCsvLimits.MaxBytes)
        {
            errors.Add("file", $"The file is larger than {TradeCsvLimits.MaxBytes / (1024 * 1024)} MB.");
        }
        else
        {
            bytes = new byte[file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        var rebuild = true;
        var rebuildText = form["rebuild"].ToString();
        if (forImport && rebuildText.Length > 0 && !bool.TryParse(rebuildText, out rebuild))
        {
            errors.Add("rebuild", "Use true or false.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<TradeUpload>(PortfolioErrors.Invalid(errors));
        }

        var digest = form["digest"].ToString().Trim();
        return Result.Success(new TradeUpload(accountId, file!.FileName, new TradeFileContent(bytes), forImport && rebuild, digest.Length == 0 ? null : digest));
    }

    private static Result<TradeUpload> Fail(string field, string message) => Result.Failure<TradeUpload>(PortfolioErrors.Invalid(field, message));
}
