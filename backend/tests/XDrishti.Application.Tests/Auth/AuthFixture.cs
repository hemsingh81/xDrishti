using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Tests.Auth;

/// <summary>Builds the auth handlers on in-memory ports and a controllable clock; seeds the owner like provisioning does.</summary>
internal sealed class AuthFixture
{
    public const string OwnerEmail = "owner@example.invalid";
    public const string OwnerPassword = "correct horse (test only)";

    public static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    public static readonly ClientInfo Client = new("203.0.113.7", "test-agent");

    public FakeTimeProvider Clock { get; } = new(Start);

    public InMemoryAuthData Data { get; } = new();

    public FakePasswordHasher Hasher { get; } = new();

    public FakeTokenGenerator Tokens { get; } = new();

    public OwnerAccount SeedOwner(string passwordHash = "h:" + OwnerPassword)
    {
        var owner = OwnerAccount.Create(OwnerEmail, "The Owner", passwordHash, Clock.GetUtcNow());
        Data.Owners.Add(owner);
        return owner;
    }

    public LoginHandler Login() =>
        new(Data, Data, Data, Data, Hasher, Tokens, Clock, NullLogger<LoginHandler>.Instance);

    public VerifySessionHandler Verify() =>
        new(Data, Data, Data, Tokens, Clock, NullLogger<VerifySessionHandler>.Instance);

    public LogoutHandler Logout() =>
        new(Data, Data, Data, Clock, NullLogger<LogoutHandler>.Instance);

    public ProvisionOwnerHandler Provision() =>
        new(Data, Data, Data, Hasher, Clock, NullLogger<ProvisionOwnerHandler>.Instance);

    public ResetOwnerPasswordHandler Reset() =>
        new(Data, Data, Data, Data, Hasher, Clock, NullLogger<ResetOwnerPasswordHandler>.Instance);

    public GetAuditHandler Audit() => new(Data);

    public async Task<LoginResult> SignInAsync()
    {
        var result = await Login().HandleAsync(new LoginCommand(OwnerEmail, OwnerPassword, Client), CancellationToken.None);
        return result.Value;
    }
}
