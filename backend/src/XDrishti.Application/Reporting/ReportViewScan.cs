using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Walks a report response once (spec P8-17): counts the figures and lists each one that breaks the state rule with the path to it, hashes every field and value in order (two reads of one request must give the same hash) and collects the field names
/// that hold an account, client, token or password word. Names, not content, are scanned for those words. The walk is bounded: a response past <see cref="MaxNodes"/> values stops being walked and says so.
/// </summary>
internal sealed partial class ReportViewScan
{
    /// <summary>The most values one walk visits.</summary>
    public const int MaxNodes = 3_000_000;

    private const int MaxDepth = 16;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    private int _nodes;

    /// <summary>Figures looked at.</summary>
    public int Metrics { get; private set; }

    /// <summary>Each figure that breaks the state rule, with its path.</summary>
    public List<string> Breaks { get; } = [];

    /// <summary>Field names with a sensitive word, distinct.</summary>
    public SortedSet<string> SensitiveNames { get; } = new(StringComparer.Ordinal);

    /// <summary>The walk stopped at the node limit.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The hash of the whole walk, in hex.</summary>
    public string Hash => Convert.ToHexString(_hash.GetCurrentHash());

    /// <summary>Walks <paramref name="root"/>.</summary>
    /// <param name="root">The response.</param>
    /// <param name="name">The name its paths start with.</param>
    public static ReportViewScan Of(object root, string name)
    {
        ArgumentNullException.ThrowIfNull(root);
        var scan = new ReportViewScan();
        scan.Visit(root, name, 0);
        return scan;
    }

    [GeneratedRegex("account|client|token|password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Sensitive();

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateOnly) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);

    private void Add(string text) => _hash.AppendData(Encoding.UTF8.GetBytes(text));

    private void Visit(object? value, string path, int depth)
    {
        if (_nodes++ >= MaxNodes || depth > MaxDepth)
        {
            Truncated = true;
            return;
        }

        if (value is null)
        {
            Add(path + "=null;");
            return;
        }

        var type = value.GetType();
        if (IsLeaf(type))
        {
            Add(path + "=" + ((value as IFormattable)?.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString()) + ";");
            return;
        }

        if (value is MetricView metric)
        {
            Metrics++;
            var problem = ReportsMetricRule.Break(metric.State, metric.Number, metric.Reason);
            if (problem is not null)
            {
                Breaks.Add(path + ": " + problem);
            }
        }

        if (value is IEnumerable items)
        {
            var i = 0;
            foreach (var item in items)
            {
                Visit(item, string.Create(CultureInfo.InvariantCulture, $"{path}[{i++}]"), depth + 1);
            }

            Add(path + "#" + i.ToString(CultureInfo.InvariantCulture) + ";");
            return;
        }

        foreach (var property in Properties.GetOrAdd(type, t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)]))
        {
            if (Sensitive().IsMatch(property.Name))
            {
                SensitiveNames.Add(property.Name);
            }

            Visit(property.GetValue(value), path + "." + property.Name, depth + 1);
        }
    }
}
