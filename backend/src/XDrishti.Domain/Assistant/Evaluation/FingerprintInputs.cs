namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>A prompt as the eval stamps it: name, version and the SHA-256 of its text.</summary>
public sealed record PromptStamp(string Name, string Version, string Sha256);

/// <summary>What a run measured, to the last byte that could change the answer (spec P9-09 AC 10): the model file, the runtime image, the prompts, the sets, the thresholds, the fixture and the profile.</summary>
public sealed record FingerprintInputs(string ModelSha256, string RuntimeDigest, IReadOnlyList<PromptStamp> Prompts, string SetsHash, string ThresholdsHash, string FixtureHash, string Profile);
