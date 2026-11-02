using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>The verdict port before P15-10: there is no protocol-F verdict, so the answer is unavailable (null) and the auto option cannot fire. P15-10 registers the real one in its place.</summary>
internal sealed class UnavailableEventFilterVerdict : IEventFilterVerdict
{
    public ValueTask<EventFilterVerdict?> NewestAsync(string ruleVersion, CancellationToken cancellationToken) => ValueTask.FromResult<EventFilterVerdict?>(null);
}
