using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Auth;

namespace XDrishti.Infrastructure.Auth;

internal sealed class TokenGenerator : ITokenGenerator
{
    private const int TokenBytes = 32;
    private static readonly byte[] CsrfPurpose = Encoding.UTF8.GetBytes("xd-csrf-v1");

    public string NewSessionToken() => ToBase64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public string DeriveCsrfToken(string sessionToken) =>
        ToBase64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(sessionToken), CsrfPurpose));

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string ToBase64Url(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
