using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Market.Bars;

/// <summary>The spelling of <see cref="DataAccountState.Status"/> (part of the API).</summary>
public static class DataAccountStatuses
{
    /// <summary>There is no primary Data account.</summary>
    public const string None = "none";
    public const string NotConnected = "notConnected";
    public const string Expired = "expired";
    public const string Connected = "connected";
}

/// <summary>The primary Data account as the pipeline needs it. <see cref="Reason"/> says, when the account cannot fetch, what the owner should do.</summary>
public sealed record DataAccountState(Guid? AccountId, string? Name, string Status, string? Reason)
{
    public bool CanFetch => Status == DataAccountStatuses.Connected;
}

public interface IDataAccountStatus
{
    ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken);

    /// <summary>The access token of the primary Data account; null when it has none that works (see <see cref="GetAsync"/>). The caller only puts it into a request header.</summary>
    ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken);
}
