using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Api.Infrastructure;
using XDrishti.Application.Lab;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.Lab;

/// <summary>
/// The downloadable folder of a Lab report (spec P5-06, ADR 0063): a zip of <c>report.json</c>, <c>report.html</c>, <c>summary.txt</c>, <c>README.txt</c> and <c>manifest.json</c> (the export library's manifest: size and SHA-256 of the other four).
/// Entry times are fixed, so equal reports give equal bytes.
/// </summary>
internal static class ReportFolder
{
    /// <summary>The manifest format of a report folder.</summary>
    public const string Format = "xd-lab-report-1";

    private static readonly DateTimeOffset EntryTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static byte[] Build(LabReportView report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["report.json"] = JsonSerializer.SerializeToUtf8Bytes(report, ApiJsonContext.Default.LabReportView),
            ["report.html"] = Utf8(ReportHtml.Render(report)),
            ["summary.txt"] = Utf8(report.Summary + "\n"),
            ["README.txt"] = Utf8(Readme(report)),
        };
        var entries = files.Select(f => new ManifestEntry(f.Key, f.Value.Length, Convert.ToHexStringLower(SHA256.HashData(f.Value)))).ToList();
        files["manifest.json"] = ExportManifest.Build(Format, report.Run.Key, null, entries, []);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = EntryTime;
                using var target = entry.Open();
                target.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static string Readme(LabReportView r) =>
        "Lab report of strategy " + Clean(r.Run.StrategyId) + ", outcome " + r.Outcome + ".\n\n"
        + "report.json   the report as the API serves it\nreport.html   the same as one static page (no script, no link)\nsummary.txt   the plain-English summary, built from the report's numbers only\nmanifest.json size and SHA-256 of the other files\n\n"
        + "Honest limits:\n" + string.Join('\n', r.HonestLimits.Select(l => "- " + Clean(l))) + "\n";

    private static string Clean(string value) => string.Concat(value.Where(c => !char.IsControl(c)));
}
