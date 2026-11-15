using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Costs</c> setting of GROSS-01 (ADR 0294): costs are gross forever (ADR 0292), so the evidence gates judge each trade's gross R less this stated buffer per round trip. Default 0.10 R (the top of the researched 0.05 to 0.12 R range of an NSE intraday round trip
/// at Dhan, slippage included); 0.05 to 0.50. A larger buffer is stricter, so raising it is free and lowering it is the owner's act. <see cref="Problems"/> lists a value out of range (the host refuses to start on one, and a gate that reads an invalid buffer never passes).
/// </summary>
/// <param name="BufferR">The cost buffer in R per round trip.</param>
public sealed record CostBufferOptions(double BufferR = CostBufferOptions.DefaultBufferR)
{
    /// <summary>The researched default.</summary>
    public const double DefaultBufferR = 0.10;

    /// <summary>The smallest buffer configuration may take.</summary>
    public const double MinBufferR = 0.05;

    /// <summary>The largest buffer configuration may take.</summary>
    public const double MaxBufferR = 0.50;

    /// <summary>The prefix of every basis label built this way.</summary>
    public const string BasisPrefix = "gross_less_buffer";

    /// <summary>Whether the value is usable.</summary>
    public bool IsValid => Problems().Count == 0;

    /// <summary>The basis label of every figure built with this buffer, such as <c>gross_less_buffer(0.10R)</c>; nothing built this way is labelled net.</summary>
    public string Label() => string.Create(CultureInfo.InvariantCulture, $"{BasisPrefix}({BufferR:0.00##}R)");

    /// <summary>The setting snapshot kept with a figure or a decision.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"Confidence:Costs:BufferR={BufferR:R}");

    /// <summary>One trade's R on this basis: its gross R less the buffer.</summary>
    public double Apply(double grossR) => grossR - BufferR;

    /// <summary>The problems of the value, empty when it is usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(BufferR) || BufferR is < MinBufferR or > MaxBufferR)
        {
            problems.Add("Costs:BufferR must be 0.05 to 0.50 R per round trip");
        }

        return problems.AsReadOnly();
    }
}
