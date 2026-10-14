using XDrishti.Application.Portfolio.Trades;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// Reads the multipart request of the trade preview and import: fields <c>accountId</c>, <c>file</c> and, for an import, <c>rebuild</c> and <c>digest</c> (the file itself is read by <see cref="CsvUploads"/>). Anything wrong is
/// <c>400 portfolio.invalid</c> with the field.
/// </summary>
internal static class TradeUploads
{
    public static async Task<Result<TradeUpload>> ReadAsync(HttpContext http, bool forImport, CancellationToken cancellationToken)
    {
        var read = await CsvUploads.ReadAsync(http, "the tradebook", cancellationToken);
        if (read.IsFailure)
        {
            return Result.Failure<TradeUpload>(read.Error);
        }

        var upload = read.Value;
        var errors = upload.Errors;
        var rebuild = true;
        var rebuildText = upload.Form["rebuild"].ToString();
        if (forImport && rebuildText.Length > 0 && !bool.TryParse(rebuildText, out rebuild))
        {
            errors.Add("rebuild", "Use true or false.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<TradeUpload>(PortfolioErrors.Invalid(errors));
        }

        var digest = upload.Form["digest"].ToString().Trim();
        return Result.Success(new TradeUpload(upload.AccountId, upload.FileName, new TradeFileContent(upload.Bytes), forImport && rebuild, digest.Length == 0 ? null : digest));
    }
}
