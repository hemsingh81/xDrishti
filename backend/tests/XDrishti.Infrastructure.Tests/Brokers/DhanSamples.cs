using System.Reflection;
using System.Text.Json;

namespace XDrishti.Infrastructure.Tests.Brokers;

/// <summary>The anonymised answers of Dhan's trade history and ledger under <c>Brokers/Samples</c> (made up, shaped like Dhan's public documentation), embedded in the test assembly.</summary>
internal static class DhanSamples
{
    public static string Text(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DhanHistory/" + name) ?? throw new InvalidOperationException("No sample " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static JsonElement Json(string name) => JsonDocument.Parse(Text(name)).RootElement.Clone();
}
