namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// One row of <c>accounts.exec_token_handover</c> (ADR 0171): the account's access token sealed with the hand-over key (<see cref="ExecHandoverOptions"/>), one row per
/// account. Read by the execution service (role <c>xd_exec</c>, SELECT only); written and deleted by the API together with the vault's access token.
/// </summary>
internal sealed class ExecTokenHandoverRecord
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    public ExecTokenHandoverRecord(Guid accountId, byte[] sealedToken, int keyVersion, DateTimeOffset createdAt)
    {
        AccountId = accountId;
        SealedToken = sealedToken;
        KeyVersion = keyVersion;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public byte[] SealedToken { get; private set; }

    public int KeyVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Replace(byte[] sealedToken, int keyVersion, DateTimeOffset createdAt)
    {
        SealedToken = sealedToken;
        KeyVersion = keyVersion;
        CreatedAt = createdAt;
    }
}
