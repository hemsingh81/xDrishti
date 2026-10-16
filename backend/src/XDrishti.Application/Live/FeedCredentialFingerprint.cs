using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Live;

/// <summary>A one-way fingerprint of the Data account's credentials, to tell "the token changed" from "the same token" without keeping or comparing the token itself. Held in memory only; never logged.</summary>
internal static class FeedCredentialFingerprint
{
    public static string Of(DataAccountCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.ClientId + "\n" + credentials.AccessToken.Reveal())));
    }
}
