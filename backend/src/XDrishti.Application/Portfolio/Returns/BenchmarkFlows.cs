namespace XDrishti.Application.Portfolio.Returns;

/// <summary>The cash flows of the portfolio's money put into the benchmark index (see <see cref="BenchmarkReturns.Flows"/>); empty unless <see cref="Gap"/> is <see cref="BenchmarkAvailability.Available"/>.</summary>
public sealed record BenchmarkFlows(IReadOnlyList<CashFlow> Flows, decimal ClosingValue, DateOnly? AsOf, BenchmarkAvailability Gap);
