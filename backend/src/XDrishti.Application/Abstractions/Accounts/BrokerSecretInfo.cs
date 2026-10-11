using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>That a secret is set and when it last changed. Carries no part of the value.</summary>
public sealed record BrokerSecretInfo(BrokerSecretName Name, DateTimeOffset UpdatedAt);
