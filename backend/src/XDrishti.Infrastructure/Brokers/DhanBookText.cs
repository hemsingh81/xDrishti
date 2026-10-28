namespace XDrishti.Infrastructure.Brokers;

/// <summary>Broker text kept as data: control characters out, length capped (spec P11-01). Pure.</summary>
internal static class DhanBookText
{
    public static string? Cap(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var clean = new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        return clean.Length == 0 ? null : clean.Length <= max ? clean : clean[..max];
    }
}
