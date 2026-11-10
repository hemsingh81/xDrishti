using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Planning;

/// <summary>
/// What the system may suggest (spec AU-16, ADR 0244): the baskets and the individual instruments the owner chose. At least one entry; ids are kept sorted and without duplicates so that the same choice always has the same <see cref="Hash"/>.
/// A filter only: it never changes a gate or the ranking, and nothing here places an order.
/// </summary>
public sealed record AutoScope
{
    /// <summary>The most entries (baskets plus instruments) one scope holds.</summary>
    public const int MaxEntries = 500;

    public const string EmptyCode = "auto.scope_empty";

    public const string TooLargeCode = "auto.scope_too_large";

    private AutoScope(IReadOnlyList<Guid> basketIds, IReadOnlyList<Guid> instrumentIds)
    {
        BasketIds = basketIds;
        InstrumentIds = instrumentIds;
    }

    /// <summary>The baskets, sorted.</summary>
    public IReadOnlyList<Guid> BasketIds { get; }

    /// <summary>The individual instruments (master ids), sorted.</summary>
    public IReadOnlyList<Guid> InstrumentIds { get; }

    /// <summary>SHA-256 (hex) of the sorted ids; stored on the plan built under this scope.</summary>
    public string Hash => HashOf(BasketIds, InstrumentIds);

    /// <summary>The scope of these ids, or a failure when there is none or too many.</summary>
    public static Result<AutoScope> Create(IEnumerable<Guid>? basketIds, IEnumerable<Guid>? instrumentIds)
    {
        var baskets = (basketIds ?? []).Where(id => id != Guid.Empty).Distinct().Order().ToList();
        var instruments = (instrumentIds ?? []).Where(id => id != Guid.Empty).Distinct().Order().ToList();
        if (baskets.Count + instruments.Count == 0)
        {
            return Result.Failure<AutoScope>(Error.Validation(EmptyCode, "The scope needs at least one basket or one stock."));
        }

        return baskets.Count + instruments.Count > MaxEntries
            ? Result.Failure<AutoScope>(Error.Validation(TooLargeCode, string.Create(CultureInfo.InvariantCulture, $"The scope holds at most {MaxEntries} baskets and stocks.")))
            : Result.Success(new AutoScope(baskets, instruments));
    }

    /// <summary>The hash of these ids, sorted as <see cref="Create"/> sorts them.</summary>
    public static string HashOf(IEnumerable<Guid> basketIds, IEnumerable<Guid> instrumentIds)
    {
        var text = "b:" + string.Join(',', basketIds.Order().Select(g => g.ToString("N", CultureInfo.InvariantCulture))) + ";i:" + string.Join(',', instrumentIds.Order().Select(g => g.ToString("N", CultureInfo.InvariantCulture)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
