namespace XDrishti.Domain.Learning;

/// <summary>Where a hypothesis came from (spec P6-10). The LLM source arrives with P9.</summary>
public enum HypothesisSource
{
    /// <summary>The owner wrote it.</summary>
    Owner,

    /// <summary>A value from the deterministic grid around the current value (<see cref="HypothesisGrid"/>).</summary>
    Grid,
}
