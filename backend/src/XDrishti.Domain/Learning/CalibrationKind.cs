namespace XDrishti.Domain.Learning;

/// <summary>How a raw score was turned into a probability (CF-10 part 10a, ADR 0214): chosen by how many calibration rows there were.</summary>
public enum CalibrationKind
{
    /// <summary>No usable calibration row: the constant 0.5 (called base rate in the code, but with no rows there is no rate to estimate).</summary>
    BaseRate = 0,

    /// <summary>Platt scaling: a sigmoid of the raw score with two fitted parameters, for fewer than the isotonic cut.</summary>
    Platt = 1,

    /// <summary>Isotonic regression (pool adjacent violators): a step-like map with up to one step per distinct score, for enough rows.</summary>
    Isotonic = 2,
}

/// <summary>The stored spelling of a <see cref="CalibrationKind"/>.</summary>
public static class CalibrationKindExtensions
{
    /// <summary>The lower-case code used in artifacts and reports: <c>base_rate</c>, <c>platt</c>, <c>isotonic</c>.</summary>
    public static string Code(this CalibrationKind kind) => kind switch
    {
        CalibrationKind.BaseRate => "base_rate",
        CalibrationKind.Platt => "platt",
        CalibrationKind.Isotonic => "isotonic",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The kind of a stored code, or null when the code is not one of the three.</summary>
    public static CalibrationKind? FromCode(string? code) => code switch
    {
        "base_rate" => CalibrationKind.BaseRate,
        "platt" => CalibrationKind.Platt,
        "isotonic" => CalibrationKind.Isotonic,
        _ => null,
    };
}
