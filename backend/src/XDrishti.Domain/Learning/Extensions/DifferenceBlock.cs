namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One cluster (a day or a season) of a difference of two means: the sum and count of group A and of group B inside it.</summary>
public readonly record struct DifferenceBlock(double SumA, int CountA, double SumB, int CountB);
