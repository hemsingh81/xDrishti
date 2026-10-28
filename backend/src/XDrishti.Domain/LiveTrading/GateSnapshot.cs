using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.LiveTrading;

/// <summary>The gates frozen at the moment a step was raised on them (spec P11-04 criterion 7): canonical text and its SHA-256, stored on the ramp event so the evidence of a raise can be read, and checked, later.</summary>
public sealed record GateSnapshot(string Text, string Hash, bool Eligible, IReadOnlyList<RampGate> Gates)
{
    public static GateSnapshot From(int step, IReadOnlyList<RampGate> gates)
    {
        ArgumentNullException.ThrowIfNull(gates);
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"ramp-gates/1|step={step}");
        foreach (var g in gates)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{g.Code}|{g.State}|{Num(g.Number)}|{Num(g.Threshold)}|{(g.N is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")}|{g.Reason}");
        }

        var value = text.ToString();
        return new(value, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))), RampGates.Eligible(gates), gates);
    }

    private static string Num(double? v) => v is { } x ? x.ToString("0.######", CultureInfo.InvariantCulture) : "-";
}
