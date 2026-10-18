using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>An instrument's tick size in rupees. The instrument master gives paise; <see cref="FromMasterPaise"/> is the only place that converts.</summary>
public sealed record TickSpec
{
    /// <summary>The NSE equity tick, used when the master has none.</summary>
    public const decimal FallbackSize = 0.05m;

    private const decimal PaisePerRupee = 100m;

    private TickSpec(decimal size, bool fromFallback)
    {
        Size = size;
        FromFallback = fromFallback;
    }

    public decimal Size { get; }

    public bool FromFallback { get; }

    /// <summary>The master's <c>TICK_SIZE</c> is in paise (1, 5, 10 are 0.01, 0.05, 0.10). Null gives the fallback; a result outside (0, 1] rupee is refused, not replaced.</summary>
    public static Result<TickSpec> FromMasterPaise(decimal? paise) =>
        paise is null ? Result.Success(new TickSpec(FallbackSize, true)) : Check(paise.Value / PaisePerRupee);

    /// <summary>For callers that already hold rupees. Same range as <see cref="FromMasterPaise"/>.</summary>
    public static Result<TickSpec> FromRupees(decimal size) => Check(size);

    private static Result<TickSpec> Check(decimal rupees) =>
        rupees > 0 && rupees <= 1
            ? Result.Success(new TickSpec(rupees, false))
            : Result.Failure<TickSpec>(SimulationErrors.TickSizeInvalid($"{rupees.ToString(CultureInfo.InvariantCulture)} rupees is not above 0 and at most 1"));
}
