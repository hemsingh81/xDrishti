using System.Globalization;
using System.Text;
using XDrishti.Api.Simulate.Assist;
using XDrishti.SimExport.Verification;
using XDrishti.SimReader;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Builders and fakes of the assisted-check tests (P3-09c): one invented ORB day, a pack.json and engine-values.csv to match, an in-memory pack folder that records the order of reads and writes, and fake sources.</summary>
internal static class AssistSupport
{
    public const string Symbol = "NSE:AAA";

    public static readonly DateOnly Date = new(2026, 3, 2);

    private static readonly DateTimeOffset Midnight = new(2026, 3, 2, 0, 0, 0, TimeSpan.FromHours(5.5));

    public static long Epoch(string hhmm) => Midnight.ToUnixTimeSeconds() + (((int.Parse(hhmm[..2], CultureInfo.InvariantCulture) * 60) + int.Parse(hhmm[3..], CultureInfo.InvariantCulture)) * 60L);

    /// <summary>A flat day at 100 with an opening range 99 to 101, a 09:30 bar that closes 102.5 (a Buy), then flat 102.5 to the close: entry 102.5 at 09:45, stop 99, target 109.5, square-off at 15:15.</summary>
    public static List<ReaderBar> BuyDay()
    {
        var bars = new List<ReaderBar>();
        for (var m = (9 * 60) + 15; m < (15 * 60) + 30; m++)
        {
            var price = m >= (9 * 60) + 45 ? 102.5m : 100m;
            var at = Midnight.ToUnixTimeSeconds() + (m * 60L);
            bars.Add(m switch
            {
                (9 * 60) + 20 => new ReaderBar(at, 100m, 101m, 100m, 100m, 1000m),
                (9 * 60) + 25 => new ReaderBar(at, 100m, 100m, 99m, 100m, 1000m),
                (9 * 60) + 44 => new ReaderBar(at, 100m, 103m, 100m, 102.5m, 1000m),
                _ => new ReaderBar(at, price, price, price, price, 1000m),
            });
        }

        return bars;
    }

    public static ReaderReading Read(IReadOnlyList<ReaderBar> bars) => ChartReader.Read(bars, ReaderStrategy.OrbV1(0.05m), ReaderSide.Buy)!;

    public static string TradeKey(string symbol = Symbol) => "ORB|" + symbol + "|Buy|2026-03-02";

    public static byte[] PackJson(params string[] symbols)
    {
        var rows = symbols.Select((s, i) => $"{{\"seq\":{i + 1},\"trade_key\":\"{TradeKey(s)}\",\"symbol\":\"{s}\",\"side\":\"Buy\",\"date\":\"2026-03-02\",\"run_key\":\"rk\"}}");
        return Encoding.UTF8.GetBytes(
            "{\"format\":\"" + PackFiles.Format + "\",\"pack_id\":\"t-1\",\"seed\":\"seed-1\",\"runs\":[{\"strategy\":\"ORB\",\"parameters\":{\"or_min\":\"15\",\"rr\":\"2\"},\"run_key\":\"rk\"}],\"rows\":[" + string.Join(',', rows) + "]}");
    }

    /// <summary>The engine's side for the rows, equal to the reading unless <paramref name="change"/> alters a column.</summary>
    public static byte[] EngineValues(ReaderReading reading, string[] symbols, Action<Dictionary<string, string>>? change = null)
    {
        var text = new StringBuilder(string.Join(',', PackFiles.EngineValueColumns)).Append('\n');
        for (var i = 0; i < symbols.Length; i++)
        {
            var cells = PackFiles.EngineValueColumns.ToDictionary(c => c, _ => string.Empty);
            cells["seq"] = (i + 1).ToString(CultureInfo.InvariantCulture);
            cells["trade_key"] = TradeKey(symbols[i]);
            cells["source"] = "random";
            cells["tick"] = "0.05";
            cells["entry_ist"] = AssistLog.Time(reading.EntryEpoch);
            cells["entry_price"] = AssistLog.Number(reading.EntryPrice);
            cells["stop_price"] = AssistLog.Number(reading.Stop);
            cells["target_price"] = AssistLog.Number(reading.Target);
            cells["exit_ist"] = AssistLog.Time(reading.ExitEpoch);
            cells["exit_price"] = AssistLog.Number(reading.ExitPrice);
            cells["exit_reason"] = reading.ExitReason.Code();
            cells["r_exact"] = AssistLog.Number(Math.Round(reading.SeenR, 4));
            change?.Invoke(cells);
            text.Append(string.Join(',', PackFiles.EngineValueColumns.Select(c => cells[c]))).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    public static byte[] Manifest(byte[] engine) =>
        XDrishti.SimExport.Manifest.ExportManifest.Build(
            PackFiles.Format,
            new string('a', 64),
            null,
            [new XDrishti.SimExport.Manifest.ManifestEntry(PackFiles.EngineValuesName, engine.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(engine)))],
            []);

    public static MemoryFolder Folder(string[] symbols, byte[]? engine = null)
    {
        var folder = new MemoryFolder();
        folder.Files[PackFiles.PackJsonName] = PackJson(symbols);
        folder.Files[PackFiles.EngineValuesName] = engine ?? EngineValues(Read(BuyDay()), symbols);
        folder.Files[PackFiles.CheckLogName] = Encoding.UTF8.GetBytes("seq,trade_key\n1,x\n");
        folder.Files["manifest.json"] = Manifest(folder.Files[PackFiles.EngineValuesName]);
        return folder;
    }

    /// <summary>An in-memory pack folder; <see cref="Ops"/> lists every call in order.</summary>
    internal sealed class MemoryFolder : IPackFolder
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public List<string> Ops { get; } = [];

        public byte[]? Read(string name)
        {
            Ops.Add("read " + name);
            return Files.GetValueOrDefault(name);
        }

        public void Write(string name, byte[] bytes)
        {
            Ops.Add("write " + name);
            Files[name] = bytes;
        }
    }

    internal sealed class FakeStored(IReadOnlyList<ReaderBar> bars) : IStoredDays
    {
        public ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken) => ValueTask.FromResult(new StoredDay(0.05m, bars));
    }

    internal sealed class FakeSecond(Func<string, SecondDay> answer, string name = "fake") : ISecondSource
    {
        public List<string> Calls { get; } = [];

        public string Name => name;

        public ValueTask<SecondDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
        {
            Calls.Add(symbol + " " + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return ValueTask.FromResult(answer(symbol));
        }
    }
}
