using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Bars.Review;

namespace XDrishti.Application.Tests.Market.Bars.Review;

/// <summary>The fakes behind the review step (folder, log, master list, comparison) and the pieces built on them.</summary>
internal sealed class CsvReviewHarness
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(Now);

    public FakeCsvFolder Folder { get; } = new();

    public FakeCsvFileLog Log { get; } = new();

    public FakeListedInstruments Listed { get; } = new();

    public FakeBarComparison Comparison { get; } = new();

    public FakeDataSettings Settings { get; } = new();

    public CsvFileCatalog Catalog => new(Folder, Log, Listed);

    public CsvFilePreviewer Previewer => new(Folder, Comparison, Settings, Clock);

    public ListCsvFilesHandler List => new(Catalog, Folder);

    public PreviewCsvFilesHandler Preview => new(Catalog, Previewer, Settings, Clock);

    /// <summary>Adds a regular-session file (<paramref name="days"/> days of 375 minutes from 2024-04-01) for a symbol the master list knows; returns the instrument id.</summary>
    public Guid AddFile(string symbol, int days = 1, string suffix = ".csv.gz", DateTimeOffset? modified = null, long shiftSeconds = 0)
    {
        Folder.Add($"{symbol}_1m{suffix}", CsvFixtures.Csv(CsvFixtures.Times(days, shiftSeconds: shiftSeconds)), modified);
        return Listed.Matches.TryGetValue($"NSE:{symbol}", out var ids) ? ids[0] : Listed.Add($"NSE:{symbol}");
    }
}
