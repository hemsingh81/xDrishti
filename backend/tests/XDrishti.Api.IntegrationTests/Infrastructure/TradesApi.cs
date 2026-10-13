using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls of the trade API shared by the tests. Symbols, ISINs and prices are made up.</summary>
internal static class TradesApi
{
    public const string Isin = "INE000A01011";

    public const string Header = "date,symbol,isin,side,quantity,price,trade id";

    /// <summary>The base tradebook: two purchases and a sale, inside the week the tests store day bars for (Mon 5 to Fri 9 October 2026).</summary>
    public static readonly string[] BaseRows =
    [
        "2026-10-05,EXMPL,INE000A01011,buy,10,90,T1",
        "2026-10-06,EXMPL,INE000A01011,buy,5,110,T2",
        "2026-10-08,EXMPL,INE000A01011,sell,3,130,T3",
    ];

    public static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    public static MultipartFormDataContent Upload(Guid accountId, string csv, bool? rebuild = null, string? digest = null, string fileName = "tradebook.csv")
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(accountId.ToString()), "accountId" },
        };
        if (rebuild is { } r)
        {
            content.Add(new StringContent(r ? "true" : "false"), "rebuild");
        }

        if (digest is not null)
        {
            content.Add(new StringContent(digest), "digest");
        }

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return content;
    }

    public static async Task<HttpResponseMessage> PreviewRawAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        using var content = Upload(accountId, csv);
        return await client.PostAsync(new Uri("/api/portfolio/trades/preview", UriKind.Relative), content, cancellationToken);
    }

    public static async Task<TradePreviewReport> PreviewTradesAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        using var response = await client.PreviewRawAsync(accountId, csv, cancellationToken);
        return await response.ReadAsync<TradePreviewReport>(cancellationToken);
    }

    public static async Task<HttpResponseMessage> ImportRawAsync(this HttpClient client, Guid accountId, string csv, bool? rebuild, string? digest, CancellationToken cancellationToken)
    {
        using var content = Upload(accountId, csv, rebuild, digest);
        return await client.PostAsync(new Uri("/api/portfolio/trades/import", UriKind.Relative), content, cancellationToken);
    }

    /// <summary>Imports with a rebuild, sending the digest of a fresh preview.</summary>
    public static async Task<TradeImportResult> ImportTradesAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        var digest = (await client.PreviewTradesAsync(accountId, csv, cancellationToken)).Digest;
        using var response = await client.ImportRawAsync(accountId, csv, rebuild: true, digest, cancellationToken);
        return await response.ReadAsync<TradeImportResult>(cancellationToken);
    }

    public static async Task<TradeImportResult> ImportTradesOnlyAsync(this HttpClient client, Guid accountId, string csv, CancellationToken cancellationToken)
    {
        using var response = await client.ImportRawAsync(accountId, csv, rebuild: false, digest: null, cancellationToken);
        return await response.ReadAsync<TradeImportResult>(cancellationToken);
    }

    public static async Task<TradeUndoResult> UndoAsync(this HttpClient client, Guid batchId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/portfolio/trade-batches/{batchId}/undo", cancellationToken);
        return await response.ReadAsync<TradeUndoResult>(cancellationToken);
    }

    public static async Task<HoldingRebuildResult> RebuildLotsAsync(this HttpClient client, Guid holdingId, bool dryRun, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/holdings/{holdingId}/rebuild-lots?dryRun={(dryRun ? "true" : "false")}", cancellationToken);
        return await response.ReadAsync<HoldingRebuildResult>(cancellationToken);
    }

    public static async Task<TradePage> HoldingTradesAsync(this HttpClient client, Guid holdingId, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/holdings/{holdingId}/trades{query}", cancellationToken);
        return await response.ReadAsync<TradePage>(cancellationToken);
    }

    public static async Task<TradeBatchPage> BatchesAsync(this HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/portfolio/trade-batches" + query, cancellationToken);
        return await response.ReadAsync<TradeBatchPage>(cancellationToken);
    }

    /// <summary>The body of a problem response as JSON.</summary>
    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }
}
