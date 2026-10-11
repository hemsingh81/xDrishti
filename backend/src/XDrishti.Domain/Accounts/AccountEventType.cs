namespace XDrishti.Domain.Accounts;

/// <summary>What happened, for the account audit trail. Stored by name — never rename a value.</summary>
public enum AccountEventType
{
    AccountCreated,
    ConnectionUpdated,
    RolesChanged,
    PrimaryDataChanged,
    TradingUpdated,
    PortfolioUpdated,
    DataUpdated,
    AccountArchived,
    RiskProfileCreated,
    RiskProfileUpdated,
    RiskProfileDeleted,
    BucketAdded,
    SecretsReplaced,
    BrokerConnected,
    BrokerConnectFailed,
    BrokerDisconnected,
}
