using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Portfolio.Reports;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls of the report import API shared by the tests (ADR 0029). Names, ISINs and amounts are made up.</summary>
internal static class ReportsApi
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public const string GtrHeader = "Date,Scrip Name,Exchange,Bill No.,Buy Qty.,Buy Value,Sell Qty.,Sell Value,Brokerage,GST,STT,SEBI Fees,Stamp Duty,Txn. Charges,Oth. Charges,Gross Amount";

    public static string Gtr(string code, params string[] rows) =>
        string.Join('\n', ["Global transction report,From 01-10-2026 to 12-10-2026", "Name,ZZ-SENTINEL-NAME", $"UCC,{code}", "Mobile,ZZ-SENTINEL-MOBILE", "Email ID,ZZ-SENTINEL-EMAIL", string.Empty, GtrHeader, .. rows, string.Empty, "Net P&L,0.00,Brokerage,0.00,Gross P&L,0.00,Total Charges,0.00", string.Empty, "NOTE : This sheet was downloaded at 12-10-2026"]) + "\n";

    public static string Row(string date, string name, string bill, long buyQty, string buyValue, long sellQty, string sellValue, string charges = "0.00") =>
        string.Join(',', new[] { date + " 00:00:00", name, "NSE", bill, buyQty.ToString(System.Globalization.CultureInfo.InvariantCulture), buyValue, sellQty.ToString(System.Globalization.CultureInfo.InvariantCulture), sellValue, charges, "0.00", "0.00", "0.00", "0.00", "0.00", "0.00", "0.00" }.Select(c => "\"" + c + "\""));

    public static MultipartFormDataContent Upload(Guid accountId, IEnumerable<(string Name, byte[] Bytes)> files, IReadOnlyList<string?>? digests = null, object? choices = null, bool? rebuild = null, IEnumerable<int>? acceptOtherAccount = null)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(accountId.ToString()), "accountId" },
        };
        foreach (var (name, bytes) in files)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "files", name);
        }

        if (digests is not null)
        {
            content.Add(new StringContent(JsonSerializer.Serialize(digests, Web)), "digests");
        }

        if (choices is not null)
        {
            content.Add(new StringContent(JsonSerializer.Serialize(choices, Web)), "choices");
        }

        if (rebuild is { } r)
        {
            content.Add(new StringContent(r ? "true" : "false"), "rebuild");
        }

        if (acceptOtherAccount is not null)
        {
            content.Add(new StringContent(JsonSerializer.Serialize(acceptOtherAccount, Web)), "acceptOtherAccount");
        }

        return content;
    }

    public static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    public static async Task<HttpResponseMessage> PreviewImportsRawAsync(this HttpClient client, Guid accountId, IEnumerable<(string Name, byte[] Bytes)> files, CancellationToken cancellationToken)
    {
        using var content = Upload(accountId, files);
        return await client.PostAsync(new Uri("/api/portfolio/imports/preview", UriKind.Relative), content, cancellationToken);
    }

    public static async Task<ImportPreviewResponse> PreviewImportsAsync(this HttpClient client, Guid accountId, IEnumerable<(string Name, byte[] Bytes)> files, CancellationToken cancellationToken, object? choices = null)
    {
        using var content = Upload(accountId, files, choices: choices);
        using var response = await client.PostAsync(new Uri("/api/portfolio/imports/preview", UriKind.Relative), content, cancellationToken);
        return await response.ReadAsync<ImportPreviewResponse>(cancellationToken);
    }

    public static async Task<HttpResponseMessage> ImportReportsRawAsync(this HttpClient client, Guid accountId, IEnumerable<(string Name, byte[] Bytes)> files, IReadOnlyList<string?>? digests, CancellationToken cancellationToken, object? choices = null, bool? rebuild = null, IEnumerable<int>? acceptOtherAccount = null)
    {
        using var content = Upload(accountId, files, digests, choices, rebuild, acceptOtherAccount);
        return await client.PostAsync(new Uri("/api/portfolio/imports/import", UriKind.Relative), content, cancellationToken);
    }

    /// <summary>Previews the files and imports them with the digests that came back.</summary>
    public static async Task<ImportRunResponse> ImportReportsAsync(this HttpClient client, Guid accountId, IReadOnlyList<(string Name, byte[] Bytes)> files, CancellationToken cancellationToken, object? choices = null, bool? rebuild = null)
    {
        var preview = await client.PreviewImportsAsync(accountId, files, cancellationToken, choices);
        using var response = await client.ImportReportsRawAsync(accountId, files, [.. preview.Files.Select(f => f.Digest)], cancellationToken, choices, rebuild);
        return await response.ReadAsync<ImportRunResponse>(cancellationToken);
    }

    public static async Task<CostBatchPage> CostBatchesAsync(this HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/portfolio/cost-batches" + query, cancellationToken);
        return await response.ReadAsync<CostBatchPage>(cancellationToken);
    }

    public static async Task<CostUndoResult> UndoCostBatchAsync(this HttpClient client, Guid batchId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/portfolio/cost-batches/{batchId}/undo", cancellationToken);
        return await response.ReadAsync<CostUndoResult>(cancellationToken);
    }
}
