using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The ledger families of P15-10 and their protocols: <c>filter:results</c> (F), <c>ext:ev-1</c> and <c>ext:fu-1</c> (X), <c>ext:llm-1</c> (L, then X) and <c>pead</c> (P). A trial of a family is a canonical text, its SHA-256 and a deterministic ledger run id.</summary>
public static class ExtensionFamilies
{
    /// <summary>The event-risk filter.</summary>
    public const string Filter = "filter:results";

    /// <summary>The event features.</summary>
    public const string EventFeatures = "ext:ev-1";

    /// <summary>The fundamental features.</summary>
    public const string FundamentalFeatures = "ext:fu-1";

    /// <summary>The LLM announcement flag.</summary>
    public const string Flag = "ext:llm-1";

    /// <summary>Post-earnings drift.</summary>
    public const string Pead = "pead";

    /// <summary>The five families, in the order the verb lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Filter, EventFeatures, FundamentalFeatures, Flag, Pead];

    /// <summary>Whether the text names a family of this slice.</summary>
    public static bool IsKnown(string? family) => family is not null && All.Contains(family, StringComparer.Ordinal);

    /// <summary>The extension version of an <c>ext:</c> family (<c>ev-1</c>), or null.</summary>
    public static string? ExtensionVersionOf(string family) => family.StartsWith(ExtensionTrial.FamilyPrefix, StringComparison.Ordinal) ? family[ExtensionTrial.FamilyPrefix.Length..] : null;

    /// <summary>The trial of a variant of a non-extension family: the text identifies the variant (not the data), so the count of looks does not grow when the same variant is run again; the run id includes the data window, so a new window is a new row with the same hash.</summary>
    public static ExtensionTrialId TrialOf(string family, string text, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(family + "\n" + text)));
        var window = string.Create(CultureInfo.InvariantCulture, $"p15-ledger/1\n{hash}\n{from:yyyy-MM-dd}\n{to:yyyy-MM-dd}");
        return new ExtensionTrialId(family, text, hash, new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(window)).AsSpan(0, 16)));
    }

    /// <summary>The SHA-256 of the text as lower-case hex: the recipe hash and the run-key hash of a record.</summary>
    public static string Sha256Hex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
