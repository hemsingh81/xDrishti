namespace XDrishti.Domain.Research;

/// <summary>
/// The record of an instrument whose pre-seam (Dhan, raw) bars were admitted to a run (CF-04d, ADR 0273): the India date of its seam and the basis hash the verification rests on (<c>SeamBasis.Of</c>). Part of the run key (<c>seam=</c> line) and of the instrument's record; absent for every
/// instrument that is not admitted, so every record, key and label of such a run is what it was.
/// </summary>
public sealed record PreSeamRecord(DateOnly SeamDate, string BasisSha256);
