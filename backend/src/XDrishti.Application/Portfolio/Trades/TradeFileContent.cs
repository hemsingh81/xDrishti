using System.Security.Cryptography;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>The bytes of an uploaded tradebook, held in memory (at most <see cref="TradeCsvLimits.MaxBytes"/>): read as often as needed, never written anywhere.</summary>
public sealed class TradeFileContent(byte[] bytes)
{
    public int Length => bytes.Length;

    /// <summary>A fresh read-only stream over the bytes.</summary>
    public Stream Open() => new MemoryStream(bytes, writable: false);

    /// <summary>Lower-case hex SHA-256 of the bytes (64 characters).</summary>
    public string Sha256() => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
