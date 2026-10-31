namespace XDrishti.Domain.Accounts;

/// <summary>
/// What a <see cref="SecretCipher"/> key is for. Each purpose derives its own AES key (own HKDF salt) from its own key material, so a value sealed for one
/// purpose never opens with another (ADR 0008, ADR 0171).
/// </summary>
public enum SecretCipherPurpose
{
    /// <summary>The broker vault (<c>accounts.broker_secrets</c>, key <c>BrokerVault:Key</c>).</summary>
    BrokerVault,

    /// <summary>The live token hand-over to the execution service (<c>accounts.exec_token_handover</c>, key <c>ExecHandover:Key</c>).</summary>
    ExecHandover,
}
