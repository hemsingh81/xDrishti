using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Api.Lab;
using XDrishti.Application.Lab;

namespace XDrishti.Api.IntegrationTests.Lab;

/// <summary>Spec P5-06 criteria 5 and 6: the page escapes untrusted text and is inert, the folder is a deterministic zip whose manifest matches its files.</summary>
public sealed class ReportRenderingTests
{
    private const string Evil = "<script>alert(1)</script>\"'‮&";

    private static LabReportView Report(string text = "ORB") => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), "FAIL", 1, 1,
        new ReportRunView(new string('1', 64), text, 1, new string('a', 64), "robust", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), text, 1, [text]),
        new ReportKpisView(10, 0.1, null, 3, 4, 2),
        [new GateView("oos_trades", 300, 300, 10, false, text)],
        [new CellView(text, "15m", "BUY", "d", "c", null, true, text, 3, 3, 10, 0.1, null)],
        [],
        null,
        [new ReportWarningView("low_samples", text)],
        [text],
        [text],
        [text],
        text);

    [Fact]
    public void Untrusted_text_comes_out_inert_in_the_page()
    {
        var page = ReportHtml.Render(Report(Evil));

        page.ShouldNotContain("<script");
        page.ShouldNotContain("‮");
        page.ShouldContain("&lt;script&gt;");
        page.ShouldContain("Content-Security-Policy");
        page.ShouldContain("default-src");
        page.ShouldNotContain("<a ");
        page.ShouldNotContain("<img");
        page.ShouldNotContain("<iframe");
    }

    [Fact]
    public void The_same_report_gives_the_same_page_and_the_same_zip_bytes()
    {
        ReportHtml.Render(Report()).ShouldBe(ReportHtml.Render(Report()));
        ReportFolder.Build(Report()).ShouldBe(ReportFolder.Build(Report()));
    }

    [Fact]
    public void The_folder_holds_five_files_and_the_manifest_lists_the_others_with_their_hashes()
    {
        using var zip = new ZipArchive(new MemoryStream(ReportFolder.Build(Report(Evil))), ZipArchiveMode.Read);

        zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ShouldBe(["README.txt", "manifest.json", "report.html", "report.json", "summary.txt"]);
        var manifest = JsonDocument.Parse(Read(zip, "manifest.json"));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToList();
        files.Count.ShouldBe(4);
        foreach (var f in files)
        {
            var name = f.GetProperty("name").GetString()!;
            var bytes = Encoding.UTF8.GetBytes(Read(zip, name));
            f.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)), name);
        }
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
