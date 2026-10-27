namespace XDrishti.Application.Abstractions.Ai;

/// <summary>What the local model server is doing, as far as its HTTP answers tell (spec P9-01). Never a guess: no answer is <see cref="Down"/>.</summary>
public enum LlmState
{
    /// <summary>Answering, the key is accepted and the expected model is served.</summary>
    Ready,

    /// <summary>The process runs and is still loading the model.</summary>
    Loading,

    /// <summary>Nothing answers (not started, switched off in this deployment, or too slow).</summary>
    Down,

    /// <summary>It answers, but the key is missing or rejected, or it serves another model than the one expected.</summary>
    Misconfigured,
}
