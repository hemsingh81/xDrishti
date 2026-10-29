using System.Globalization;
using XDrishti.Domain.Common;

namespace XDrishti.Execution.Domain;

/// <summary>
/// Our own idempotent id of one order leg: <c>XD</c> + yyMMdd + 6 base-36 digits of the intent + a kind letter (E S T X F) + an attempt 1 to 9, 16 characters of [A-Z0-9]. Deterministic; an entry's attempt is always 1.
/// </summary>
public readonly record struct ClientOrderId(DateOnly SessionDate, long IntentId, LegKind Kind, int Attempt)
{
    public const long MaxIntentId = 2_176_782_335; // 36^6 - 1

    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Letters = "ESTXF";

    public static Result<ClientOrderId> Create(DateOnly sessionDate, long intentId, LegKind kind, int attempt) =>
        intentId is < 0 or > MaxIntentId || attempt is < 1 or > 9 || (kind == LegKind.Entry && attempt != 1) || sessionDate.Year is < 2000 or > 2099
            ? Result.Failure<ClientOrderId>(Error.Validation("execution.client_order_id_invalid", "The intent, attempt, kind or date is out of range (an entry is attempt 1)."))
            : Result.Success(new ClientOrderId(sessionDate, intentId, kind, attempt));

    public static bool TryParse(string? text, out ClientOrderId id)
    {
        id = default;
        if (text is not { Length: 16 } || !text.StartsWith("XD", StringComparison.Ordinal)
            || !DateOnly.TryParseExact(text.AsSpan(2, 6), "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return false;
        }

        long value = 0;
        foreach (var c in text.AsSpan(8, 6))
        {
            var d = Digits.IndexOf(c, StringComparison.Ordinal);
            if (d < 0)
            {
                return false;
            }

            value = (value * 36) + d;
        }

        var kind = Letters.IndexOf(text[14], StringComparison.Ordinal);
        if (kind < 0 || text[15] is < '1' or > '9')
        {
            return false;
        }

        var result = Create(date, value, (LegKind)kind, text[15] - '0');
        id = result.IsSuccess ? result.Value : default;
        return result.IsSuccess;
    }

    public override string ToString()
    {
        var digits = new char[6];
        var rest = IntentId;
        for (var i = 5; i >= 0; i--)
        {
            digits[i] = Digits[(int)(rest % 36)];
            rest /= 36;
        }

        return $"XD{SessionDate.ToString("yyMMdd", CultureInfo.InvariantCulture)}{new string(digits)}{Letters[(int)Kind]}{Attempt.ToString(CultureInfo.InvariantCulture)}";
    }
}
