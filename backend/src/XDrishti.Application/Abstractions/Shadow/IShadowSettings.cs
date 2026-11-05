namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>The owner's switch of the shadow record, <c>paper.shadow.enabled</c> (spec CF-08, D7): the newest row of <c>paper.shadow_settings</c>, OFF when there is none. The owner's act to change it is a later slice.</summary>
public interface IShadowSettings
{
    /// <summary>Whether the shadow services may write; false (the default) when no row is stored.</summary>
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken);
}
