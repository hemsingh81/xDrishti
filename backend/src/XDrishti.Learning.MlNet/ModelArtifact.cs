namespace XDrishti.Learning.MlNet;

/// <summary>The bytes of a trained model and their SHA-256 (lower-case hex) (P6-06, ADR 0068). The registry of P6-07 stores the bytes under <c>data/models</c> and the hash beside them.</summary>
/// <param name="Bytes">The artifact: a small header (trainer, imputation means, calibration knots, notes) followed by the ML.NET model.</param>
/// <param name="Sha256">The hash of <paramref name="Bytes"/>.</param>
public sealed record ModelArtifact(byte[] Bytes, string Sha256);
