namespace XDrishti.Domain.Options.Pricing;

/// <summary>A forward with the label of where it came from, so the label travels with the value.</summary>
public readonly record struct Forward(double Value, ForwardSource Source);
