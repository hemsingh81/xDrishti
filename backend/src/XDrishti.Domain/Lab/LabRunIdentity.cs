using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Lab;

/// <summary>
/// The identity of a Lab run (spec P5-02, D3; ADR 0058): the SHA-256 of a fixed text of the strategy hash, the universe (label and sorted instrument ids), the window, the method, the hash of the configuration that matters and the engine version.
/// The same input is the same key; nothing about time or the host is in it.
/// </summary>
public static class LabRunIdentity
{
    /// <summary>The key text, one <c>name=value</c> line each, ending with a line feed.</summary>
    public static string KeyText(string strategyHash, string universeLabel, IReadOnlyCollection<Guid> instrumentIds, DateOnly from, DateOnly to, string method, string configHash, string engineVersion)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var ids = string.Join(",", instrumentIds.Order().Select(i => i.ToString("D", CultureInfo.InvariantCulture)));
        return "xd-lab-run 1\n"
            + "strategy=" + strategyHash + "\n"
            + "universe=" + universeLabel + "|" + ids + "\n"
            + "window=" + from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".." + to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n"
            + "method=" + method + "\n"
            + "config=" + configHash + "\n"
            + "engine=" + engineVersion + "\n";
    }

    /// <summary>The key: the lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public static string Key(string strategyHash, string universeLabel, IReadOnlyCollection<Guid> instrumentIds, DateOnly from, DateOnly to, string method, string configHash, string engineVersion) =>
        Sha256(KeyText(strategyHash, universeLabel, instrumentIds, from, to, method, configHash, engineVersion));

    /// <summary>The lower-case hex SHA-256 of the UTF-8 bytes of a text.</summary>
    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
