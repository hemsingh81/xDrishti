namespace XDrishti.Domain.Learning;

/// <summary>What a registered model is for (P6-07, ADR 0070). A model's role is its latest designation; designations are only appended.</summary>
public enum ModelRole
{
    /// <summary>Registered and kept for comparison; the starting role of every model, whatever its verdict.</summary>
    Challenger,

    /// <summary>The model the system would use (advisory until P7). Only a model whose verdict is <see cref="ValidationOutcome.Eligible"/> may hold it.</summary>
    Champion,

    /// <summary>Replaced or set aside; kept, never deleted.</summary>
    Retired,
}
