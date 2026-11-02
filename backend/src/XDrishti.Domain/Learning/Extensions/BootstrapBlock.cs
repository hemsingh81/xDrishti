namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One block of the paired bootstrap (P13-05): the sum of the paired differences of its rows and how many rows it holds. A draw takes whole blocks, so rows of one block stay together.</summary>
/// <param name="Sum">The sum of the paired differences d of the block's rows.</param>
/// <param name="Count">The number of rows.</param>
public readonly record struct BootstrapBlock(double Sum, int Count);
