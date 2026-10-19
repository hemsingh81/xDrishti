namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>The TEST sheet of ADR 0042 D16, typed here from the spec table (test-synthetic-1). Invented numbers.</summary>
internal static class TestSheet
{
    internal static OracleChargeSheet Sheet { get; } = new(
        "test-synthetic-1",
        [
            new OracleSchedule(
                new DateOnly(2026, 1, 1),
                18m,
                OracleRounding.HalfAwayFromZero,
                new OracleRates(0.03m, 20m, 0m, 0m, 0.04m, 0.005m, 50m, 0.02m, 0m, 0m),
                new OracleRates(0m, 0m, 0m, 0.08m, 0.08m, 0.004m, 50m, 0.03m, 0m, 10m)),
            new OracleSchedule(
                new DateOnly(2026, 3, 3),
                18m,
                OracleRounding.HalfAwayFromZero,
                new OracleRates(0.03m, 20m, 0m, 0m, 0.035m, 0.005m, 50m, 0.02m, 0m, 0m),
                new OracleRates(0m, 0m, 0m, 0.08m, 0.09m, 0.004m, 50m, 0.03m, 0m, 10m)),
        ]);
}
