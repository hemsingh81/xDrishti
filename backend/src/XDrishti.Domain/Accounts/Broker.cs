namespace XDrishti.Domain.Accounts;

/// <summary>Supported brokers. Stored by name — never rename a value. Dhan is the only one (ADR 0009); the column stays so another can be added.</summary>
public enum Broker
{
    Dhan,
}
