namespace XDrishti.Api.Simulate.Assist;

/// <summary>The rows of <c>engine-values.csv</c> by trade key (cells by column name). The file has no quoted cell: a row that does not split into the header's columns makes the file unusable (null).</summary>
internal static class EngineValues
{
    public static Dictionary<string, IReadOnlyDictionary<string, string>>? Read(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var header = lines[0].TrimEnd('\r').Split(',');
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var cells = line.TrimEnd('\r').Split(',');
            if (cells.Length != header.Length || !result.TryAdd(cells[Array.IndexOf(header, "trade_key")], header.Zip(cells).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal)))
            {
                return null;
            }
        }

        return Array.IndexOf(header, "trade_key") < 0 ? null : result;
    }
}
