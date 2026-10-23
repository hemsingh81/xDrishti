using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The model files (P6-07, ADR 0070): content-addressed by SHA-256, written atomically, never read without checking that the bytes still hash to their name.</summary>
public interface IModelFiles
{
    /// <summary>Writes the bytes under their hash; refused when they do not hash to it; nothing is rewritten when the file is there and intact.</summary>
    ValueTask<Result> WriteAsync(string sha256, byte[] bytes, CancellationToken cancellationToken);

    /// <summary>Reads the bytes; <c>models.file_missing</c> or <c>models.file_corrupt</c> (the bytes do not hash to the name) are failures.</summary>
    ValueTask<Result<byte[]>> ReadAsync(string sha256, CancellationToken cancellationToken);
}
