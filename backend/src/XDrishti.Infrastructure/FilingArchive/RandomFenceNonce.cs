using System.Security.Cryptography;

namespace XDrishti.Infrastructure.FilingArchive;

/// <summary>The fence token of a request (P15-08): 8 random bytes from the operating system's cryptographic source as 16 lower-case hexadecimal characters, new for every request.</summary>
internal sealed class RandomFenceNonce : IFenceNonce
{
    public string Next() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
