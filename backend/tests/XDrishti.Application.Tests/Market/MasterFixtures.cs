using System.Reflection;
using System.Text;

namespace XDrishti.Application.Tests.Market;

/// <summary>The committed samples of the two source files (real rows; see <c>Fixtures/</c>), as streams.</summary>
internal static class MasterFixtures
{
    /// <summary>Dhan's scrip master sample: 75 rows, the real 33-column header. The SBIN September 2026 future (id 99001) is a copy with an expiry already past.</summary>
    public const string Master = "dhan-master-sample.csv";

    public const string Sectors = "nse-sector-sample.csv";

    /// <summary>The clock the sample is read at: after the September contract, before the October ones.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    public static Stream Open(string name) =>
        typeof(MasterFixtures).Assembly.GetManifestResourceStream("Fixtures/" + name)
        ?? throw new InvalidOperationException($"Fixture {name} is not embedded.");

    public static string Text(string name)
    {
        using var reader = new StreamReader(Open(name), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static Stream AsStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    public static Assembly Assembly => typeof(MasterFixtures).Assembly;
}
