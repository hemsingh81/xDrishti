namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>The outcome of a broker call: a value or a <see cref="BrokerFailure"/> (create with <see cref="BrokerResponse"/>).</summary>
public sealed class BrokerResponse<T>
{
    private readonly T? _value;

    internal BrokerResponse(T? value, BrokerFailure? failure)
    {
        _value = value;
        Failure = failure;
    }

    public BrokerFailure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot read the value of a failed broker call.");
}
