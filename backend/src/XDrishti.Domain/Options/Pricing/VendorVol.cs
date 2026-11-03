namespace XDrishti.Domain.Options.Pricing;

/// <summary>A vendor IV read as a fraction per year, with the unit label that travels with it (the unit is UNVERIFIED until the P14-02 pilot).</summary>
public readonly record struct VendorVol(double Fraction, string UnitLabel);
