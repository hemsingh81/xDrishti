namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// The XIRR of the same cash flows put into the benchmark index. <see cref="Rate"/> is a fraction per year (0.12 = 12 %) and set only when <see cref="State"/> is <see cref="BenchmarkState.Ok"/> or <see cref="BenchmarkState.NotMeaningful"/>.
/// <see cref="Gap"/> says why the index could not be used when <see cref="State"/> is <see cref="BenchmarkState.Unavailable"/>. <see cref="AsOf"/> is the date of the index close the closing inflow was valued at (null when it was not).
/// </summary>
public sealed record BenchmarkXirrResult(BenchmarkState State, double? Rate, BenchmarkAvailability Gap, DateOnly? AsOf = null);
