namespace XDrishti.Application.Abstractions.Live;

/// <summary>
/// The disconnect reason codes of Dhan's feed that mean the Data account's token or plan is the problem. <b>UNVERIFIED</b>: written from Dhan's documentation (801 too many connections, 802 and 808 client id,
/// 803 and 809 access token, 806 Data APIs not subscribed, 807 token expired); the owner's first real session confirms them. Any other code is an ordinary drop: the feed reconnects.
/// </summary>
public static class FeedDisconnectReasons
{
    /// <summary>True when the code says the token (or the Data API subscription) was refused: reconnecting with the same token cannot work.</summary>
    public static bool IsTokenProblem(int reason) => reason is 802 or 803 or 806 or 807 or 808 or 809;
}
