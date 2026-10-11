namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Creates <see cref="BrokerResponse{T}"/> values.</summary>
public static class BrokerResponse
{
    public static BrokerResponse<T> Ok<T>(T value) => new(value, null);

    public static BrokerResponse<T> Fail<T>(BrokerFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(default, failure);
    }

    public static BrokerResponse<T> Fail<T>(BrokerFailureKind kind, string? brokerCode = null) => Fail<T>(new BrokerFailure(kind, brokerCode));
}
