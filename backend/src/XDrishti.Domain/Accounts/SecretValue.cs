using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// A broker secret in plain text (API key, API secret, token). It never shows itself: <see cref="ToString"/> and the debugger print a
/// placeholder, so it cannot leak into logs, audit text or exception messages by accident. Only <see cref="Reveal"/> gives the value,
/// and only the vault and the broker connectors call it.
/// </summary>
[DebuggerDisplay("[redacted]")]
public sealed class SecretValue
{
    public const int MaxLength = 4096;

    public const string Redacted = "[redacted]";

    private readonly string _value;

    private SecretValue(string value) => _value = value;

    /// <summary>
    /// Checks a value entered by the owner or received from a broker: surrounding spaces are removed; it must then be 1 to
    /// <see cref="MaxLength"/> characters without spaces or control characters. The error message never contains the value.
    /// </summary>
    public static bool TryCreate(string? text, [NotNullWhen(true)] out SecretValue? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "Required.";
            return false;
        }

        if (trimmed.Length > MaxLength)
        {
            error = $"At most {MaxLength} characters.";
            return false;
        }

        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            error = "Must not contain spaces or line breaks.";
            return false;
        }

        value = new SecretValue(trimmed);
        error = null;
        return true;
    }

    /// <summary>The plain value. Never log, return or store it unencrypted.</summary>
    public string Reveal() => _value;

    public override string ToString() => Redacted;
}
