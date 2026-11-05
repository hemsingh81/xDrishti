using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>
/// CF-12 part 12a, AC12 (ADR 0218): the P6-09 drift monitors and the P6-12 counterfactual give byte-identical output with this slice. The outputs of fixed, deterministic inputs are serialised and pinned by their SHA-256; the hashes were taken from the code as it
/// stood on main before CF-12, so a change to either module (or to a type it shares with the new calculators) fails here.
/// </summary>
public sealed class Cf12GoldenTests
{
    private const long Day = 86_400;
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    private static List<DriftObservation> Series(int count, Func<int, double> r) =>
        [.. Enumerable.Range(0, count).Select(i => new DriftObservation("t" + i.ToString("D5", CultureInfo.InvariantCulture), i * Day, r(i)))];

    private static double Wave(int i) => i < 40 ? (i % 2 == 0 ? 1.3 : -0.7) : i < 100 ? -1.2 + ((i % 5) * 0.1) : 0.5 + ((i % 3) * 0.2);

    [Fact]
    public void The_p6_09_band_and_cusum_alarms_are_unchanged()
    {
        var options = new DriftOptions();
        var all = Series(200, Wave);
        var band = RollingBandDetector.MakeBand(all.Take(40).ToList(), options).Value;

        var output = new { band, rolling = RollingBandDetector.Run("c", all, 40 * Day, band, options), cusum = CusumDetector.Run("c", all, 40 * Day, band, options) };

        output.rolling.ShouldNotBeEmpty(); // the golden is over real alarms, not over nothing
        output.cusum.ShouldNotBeEmpty();
        Hash(output).ShouldBe(PinnedDrift);
    }

    [Fact]
    public void The_p6_09_calibration_alarms_are_unchanged()
    {
        var series = Enumerable.Range(0, 400).Select(i => new CalibrationObservation("k" + i.ToString("D4", CultureInfo.InvariantCulture), i * Day, 0.3 + ((i % 10) * 0.05), i % 3 == 0 ? 1 : 0)).ToList();

        var events = CalibrationDriftDetector.Run("m", series, new DriftOptions()).Value;

        events.ShouldNotBeEmpty();
        Hash(events).ShouldBe(PinnedCalibration);
    }

    [Fact]
    public void The_p6_12_counterfactual_report_is_unchanged()
    {
        var start = new DateOnly(2024, 1, 2);
        var rows = Enumerable.Range(0, 300).Select(i =>
        {
            var date = start.AddDays(i * 2);
            var f = unchecked((uint)(i * 2654435761u)) % 1000 / 1000.0;
            var win = f > 0.5;
            if (date >= new DateOnly(2024, 12, 1))
            {
                win = !win;
            }

            var epoch = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            return new ValidationRow($"S|NSE:S{i % 7}|BUY|{date:yyyy-MM-dd}|{i}", win ? 1 : 0, win ? 1.5 : -1, date, epoch, epoch + 3600, "S|15m|BUY|intraday|X|*", [f]);
        }).ToList();

        var report = CounterfactualEvaluator.Evaluate(rows, new Recent(), new CounterfactualOptions(), new ValidationOptions());

        report.IsSuccess.ShouldBeTrue();
        Hash(report.Value).ShouldBe(PinnedCounterfactual);
    }

    private sealed class Recent : IScorerFactory
    {
        public string Name => "recent";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            var last = train.OrderBy(r => r.DecisionEpochSeconds).TakeLast(30).ToList();
            var up = last.Where(r => r.Label == 1).Select(r => r.Features[0]).DefaultIfEmpty(0.5).Average();
            var down = last.Where(r => r.Label == 0).Select(r => r.Features[0]).DefaultIfEmpty(0.5).Average();
            return new Fit(up > down);
        }
    }

    private sealed class Fit(bool positive) : IRowScorer
    {
        public double Score(ValidationRow row) => positive ? 0.2 + (0.6 * row.Features[0]) : 0.8 - (0.6 * row.Features[0]);
    }

    private const string PinnedDrift = "078716f88a32e347708666f76ecc07f7cccc83b3987476dba8bf001f50e18df1";
    private const string PinnedCalibration = "7c998b399f24d72eaa22d513f2f9d3eea990ec78ece5041494efc20ee6824305";
    private const string PinnedCounterfactual = "621f429ce652fe0312aadc46b0552ac3b9740e1ec99fb3c54bac20f49a05e717";
}
