using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Portfolio.DividendImport;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls of the dividend file import API shared by the tests (ADR 0028). Names, ISINs and amounts are made up.</summary>
internal static class DividendImportApi
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public const string Header = "Date,Scrip Name,Dividend Per Share,Quantity,Dividend Paid";

    public static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    public static MultipartFormDataContent Upload(
        Guid accountId, string csv, string? digest = null, object? choices = null, IEnumerable<int>? includeDuplicates = null, string fileName = "dividend-payout.csv", string? choicesText = null)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(accountId.ToString()), "accountId" },
        };
        if (digest is not null)
        {
            content.Add(new StringContent(digest), "digest");
        }

        if (choicesText is not null)
        {
            content.Add(new StringContent(choicesText), "choices");
        }
        else if (choices is not null)
        {
            content.Add(new StringContent(JsonSerializer.Serialize(choices, Web)), "choices");
        }

        if (includeDuplicates is not null)
        {
            content.Add(new StringContent("[" + string.Join(',', includeDuplicates.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]"), "includeDuplicates");
        }

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return content;
    }

    public static async Task<HttpResponseMessage> PreviewDividendsRawAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        using var content = Upload(accountId, csv);
        return await client.PostAsync(new Uri("/api/portfolio/dividends/preview", UriKind.Relative), content, cancellationToken);
    }

    public static async Task<DividendPreviewReport> PreviewDividendsAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        using var response = await client.PreviewDividendsRawAsync(accountId, csv, cancellationToken);
        return await response.ReadAsync<DividendPreviewReport>(cancellationToken);
    }

    public static async Task<HttpResponseMessage> ImportDividendsRawAsync(
        this HttpClient client, Guid accountId, string csv, string? digest, CancellationToken cancellationToken, object? choices = null, IEnumerable<int>? includeDuplicates = null, string? choicesText = null)
    {
        using var content = Upload(accountId, csv, digest, choices, includeDuplicates, choicesText: choicesText);
        return await client.PostAsync(new Uri("/api/portfolio/dividends/import", UriKind.Relative), content, cancellationToken);
    }

    /// <summary>Imports, sending the digest of a fresh preview.</summary>
    public static async Task<DividendImportResult> ImportDividendsAsync(
        this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken, object? choices = null, IEnumerable<int>? includeDuplicates = null)
    {
        var digest = (await client.PreviewDividendsAsync(accountId, csv, cancellationToken)).Digest;
        using var response = await client.ImportDividendsRawAsync(accountId, csv, digest, cancellationToken, choices, includeDuplicates);
        return await response.ReadAsync<DividendImportResult>(cancellationToken);
    }

    public static async Task<DividendBatchPage> DividendBatchesAsync(this HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/portfolio/dividend-batches" + query, cancellationToken);
        return await response.ReadAsync<DividendBatchPage>(cancellationToken);
    }

    public static async Task<DividendUndoResult> UndoDividendBatchAsync(this HttpClient client, Guid batchId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/portfolio/dividend-batches/{batchId}/undo", cancellationToken);
        return await response.ReadAsync<DividendUndoResult>(cancellationToken);
    }
}
