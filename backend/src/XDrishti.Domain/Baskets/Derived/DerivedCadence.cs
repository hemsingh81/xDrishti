namespace XDrishti.Domain.Baskets.Derived;

/// <summary>How often a derived basket is re-formed. Weekly and monthly baskets are slow: the minimum dwell applies.</summary>
public enum DerivedCadence { Daily, Weekly, Monthly, FrozenIntraday }
