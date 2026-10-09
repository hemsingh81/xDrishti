using System.Security.Cryptography;

namespace XDrishti.DhanTrial;

/// <summary>RFC 6238 time-based one-time password (HMAC-SHA1, 30 s step) — used only for the optional unattended-token experiment.</summary>
internal static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Generate(string base32Secret, DateTimeOffset at, int digits = 6)
        => Generate(DecodeBase32(base32Secret), at, digits);

    public static string Generate(byte[] key, DateTimeOffset at, int digits = 6)
    {
        var counter = at.ToUnixTimeSeconds() / 30;
        Span<byte> message = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            message[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

#pragma warning disable CA5350 // RFC 6238 TOTP mandates HMAC-SHA1; not used for any security decision of ours.
        var hash = HMACSHA1.HashData(key, message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulus = 1;
        for (var i = 0; i < digits; i++)
        {
            modulus *= 10;
        }

        return (binary % modulus).ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static byte[] DecodeBase32(string text)
    {
        var cleaned = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in cleaned)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid base32 character in TOTP secret.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
