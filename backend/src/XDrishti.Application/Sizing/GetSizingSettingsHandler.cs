using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;

namespace XDrishti.Application.Sizing;

internal sealed class GetSizingSettingsHandler(ISizingStore store) : IQueryHandler<GetSizingSettingsQuery, SizingSettingsView>
{
    public ValueTask<SizingSettingsView> HandleAsync(GetSizingSettingsQuery query, CancellationToken cancellationToken) => SizingViews.SettingsAsync(store, cancellationToken);
}
