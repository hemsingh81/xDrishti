using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerConnectionTests
{
    private static readonly DateTimeOffset T0 = AccountData.T0;

    private static readonly Guid AccountId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    private static BrokerConnection Connected(DateTimeOffset expires, string? name = "Example Holder")
    {
        var connection = BrokerConnection.NotConnected(AccountId);
        connection.Connect(expires, "1000000001", name, T0);
        return connection;
    }

    [Fact]
    public void A_new_connection_is_not_connected()
    {
        var connection = BrokerConnection.NotConnected(AccountId);

        connection.StatusAt(T0).ShouldBe(ConnectionStatus.NotConnected);
    }

    [Fact]
    public void A_connected_token_reads_as_expired_from_its_expiry_on()
    {
        var connection = Connected(T0.AddHours(2));

        connection.StatusAt(T0.AddHours(2).AddTicks(-1)).ShouldBe(ConnectionStatus.Connected);
        connection.StatusAt(T0.AddHours(2)).ShouldBe(ConnectionStatus.Expired);
        connection.Status.ShouldBe(ConnectionStatus.Connected); // expired is never stored
        connection.LastConnectedAt.ShouldBe(T0);
    }

    [Fact]
    public void Connecting_again_without_a_name_keeps_the_name_only_for_the_same_holder()
    {
        var same = Connected(T0.AddHours(2));
        var other = Connected(T0.AddHours(2));

        same.Connect(T0.AddHours(3), "1000000001", null, T0.AddMinutes(1));
        other.Connect(T0.AddHours(3), "1000000099", null, T0.AddMinutes(1));

        same.BrokerUserName.ShouldBe("Example Holder");
        other.BrokerUserName.ShouldBeNull();
        other.BrokerUserId.ShouldBe("1000000099");
    }

    [Fact]
    public void A_failed_sign_in_keeps_the_existing_token()
    {
        var connection = Connected(T0.AddHours(2));

        connection.ConnectFailed("Sign-in failed");

        connection.StatusAt(T0).ShouldBe(ConnectionStatus.Connected);
        connection.LastError.ShouldBe("Sign-in failed");
    }

    [Fact]
    public void Disconnecting_forgets_the_token_facts_but_not_when_it_last_connected()
    {
        var connection = Connected(T0.AddHours(2));

        connection.Disconnect();

        connection.StatusAt(T0).ShouldBe(ConnectionStatus.NotConnected);
        connection.ExpiresAt.ShouldBeNull();
        connection.BrokerUserId.ShouldBeNull();
        connection.BrokerUserName.ShouldBeNull();
        connection.LastConnectedAt.ShouldBe(T0);
    }

    [Fact]
    public void Broker_texts_are_trimmed_cleaned_of_control_characters_and_limited()
    {
        var connection = Connected(T0.AddHours(2), name: "  Example\nHolder  " + new string('x', 200));

        connection.ConnectFailed(new string('e', 300));

        connection.BrokerUserName!.ShouldStartWith("ExampleHolder");
        connection.BrokerUserName!.Length.ShouldBe(BrokerConnection.MaxBrokerUserNameLength);
        connection.LastError!.Length.ShouldBe(BrokerConnection.MaxErrorLength);
    }
}
