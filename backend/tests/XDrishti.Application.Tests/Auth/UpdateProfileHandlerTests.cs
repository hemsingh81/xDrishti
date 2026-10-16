using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class UpdateProfileHandlerTests
{
    private readonly AuthFixture _fx = new();

    private async Task<AuthSession> SignedInSessionAsync()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        return _fx.Data.Sessions.Single();
    }

    private ValueTask<Result<AccountResponse>> UpdateAsync(AuthSession session, string? name, string? email) =>
        _fx.UpdateProfile().HandleAsync(new UpdateProfileCommand(session.Id, name, email, AuthFixture.Client), CancellationToken.None);

    [Fact]
    public async Task Name_and_email_are_replaced_normalised_and_returned()
    {
        var session = await SignedInSessionAsync();

        var result = await UpdateAsync(session, "  Asha Rao ", " Asha@Example.Invalid ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe("asha@example.invalid");
        result.Value.DisplayName.ShouldBe("Asha Rao");
        result.Value.CreatedAt.ShouldBe(AuthFixture.Start);
        var owner = _fx.Data.Owners.Single();
        owner.Email.ShouldBe("asha@example.invalid");
        owner.DisplayName.ShouldBe("Asha Rao");
    }

    [Fact]
    public async Task The_audit_entry_names_the_changed_fields_and_nothing_else()
    {
        var session = await SignedInSessionAsync();

        await UpdateAsync(session, "Asha Rao", "asha@example.invalid");

        var entry = _fx.Data.Events.Last();
        entry.Event.ShouldBe(AuditEventType.ProfileUpdated);
        entry.Email.ShouldBe(AuthFixture.OwnerEmail); // the address the owner signed in with
        var detail = entry.Detail.ShouldNotBeNull();
        detail.ShouldBe("changed: email, displayName");
        detail.ShouldNotContain("Asha");
    }

    [Fact]
    public async Task Changing_only_the_name_audits_only_the_name()
    {
        var session = await SignedInSessionAsync();

        await UpdateAsync(session, "Asha Rao", AuthFixture.OwnerEmail.ToUpperInvariant());

        _fx.Data.Events.Last().Detail.ShouldBe("changed: displayName");
        _fx.Data.Owners.Single().Email.ShouldBe(AuthFixture.OwnerEmail);
    }

    [Fact]
    public async Task Saving_the_same_values_succeeds_without_a_write_or_an_audit_entry()
    {
        var session = await SignedInSessionAsync();
        var events = _fx.Data.Events.Count;
        var saves = _fx.Data.SaveCount;

        var result = await UpdateAsync(session, "The Owner", AuthFixture.OwnerEmail);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.Events.Count.ShouldBe(events);
        _fx.Data.SaveCount.ShouldBe(saves);
    }

    [Fact]
    public async Task Other_sessions_the_password_and_the_lockout_counter_are_untouched()
    {
        var session = await SignedInSessionAsync();
        await _fx.SignInAsync();
        var owner = _fx.Data.Owners.Single();
        owner.RegisterFailure(_fx.Clock.GetUtcNow());

        await UpdateAsync(session, "Asha Rao", "asha@example.invalid");

        _fx.Data.Sessions.ShouldAllBe(s => s.RevokedAt == null);
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
        owner.PasswordChangedAt.ShouldBe(AuthFixture.Start);
        owner.FailedAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task The_new_email_is_what_sign_in_accepts_afterwards()
    {
        var session = await SignedInSessionAsync();
        await UpdateAsync(session, "Asha Rao", "asha@example.invalid");

        var old = await _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword, AuthFixture.Client), CancellationToken.None);
        var fresh = await _fx.Login().HandleAsync(new LoginCommand("asha@example.invalid", AuthFixture.OwnerPassword, AuthFixture.Client), CancellationToken.None);

        old.Error.ShouldBe(AuthErrors.InvalidCredentials);
        fresh.IsSuccess.ShouldBeTrue();
        fresh.Value.Session.User.DisplayName.ShouldBe("Asha Rao");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("a@b.com\nfake")]
    [InlineData("a@b.com\r\nINFO forged")]
    [InlineData("a b@c.com")]
    [InlineData("a@b\t.com")]
    [InlineData("a@b.com\0")]
    [InlineData("a@b.com\u2028forged")]
    public async Task An_invalid_email_is_refused_with_a_field_error(string? email)
    {
        var session = await SignedInSessionAsync();

        var result = await UpdateAsync(session, "Asha Rao", email);

        result.Error.Code.ShouldBe(AuthErrors.OwnerEmailInvalidCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("email");
        _fx.Data.Owners.Single().DisplayName.ShouldBe("The Owner");
    }

    [Theory]
    [InlineData("asha@example.invalid")]
    [InlineData("  asha.rao+nse@sub.example.invalid\t")]
    public async Task A_normal_email_is_accepted_and_surrounding_blanks_are_trimmed(string email)
    {
        var session = await SignedInSessionAsync();

        var result = await UpdateAsync(session, "Asha Rao", email);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe(email.Trim().ToLowerInvariant());
    }

    [Fact]
    public async Task An_email_longer_than_256_characters_is_refused_and_exactly_256_is_accepted()
    {
        var session = await SignedInSessionAsync();
        var max = new string('a', 256 - "@x.io".Length) + "@x.io";

        (await UpdateAsync(session, "Asha Rao", max + "m")).Error.Code.ShouldBe(AuthErrors.OwnerEmailInvalidCode);
        (await UpdateAsync(session, "Asha Rao", max)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_name_is_refused_with_a_field_error(string? name)
    {
        var session = await SignedInSessionAsync();

        var result = await UpdateAsync(session, name, "asha@example.invalid");

        result.Error.Code.ShouldBe(AuthErrors.OwnerNameInvalidCode);
        result.Error.Fields.ShouldNotBeNull().Single().Field.ShouldBe("displayName");
        _fx.Data.Owners.Single().Email.ShouldBe(AuthFixture.OwnerEmail);
    }

    [Fact]
    public async Task A_name_longer_than_128_characters_is_refused_and_exactly_128_is_accepted()
    {
        var session = await SignedInSessionAsync();

        (await UpdateAsync(session, new string('n', 129), "asha@example.invalid")).Error.Code.ShouldBe(AuthErrors.OwnerNameInvalidCode);
        (await UpdateAsync(session, new string('n', 128), "asha@example.invalid")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_revoked_or_unknown_session_is_unauthenticated()
    {
        var session = await SignedInSessionAsync();
        session.Revoke(_fx.Clock.GetUtcNow());

        var revoked = await UpdateAsync(session, "Asha Rao", "asha@example.invalid");
        var unknown = await _fx.UpdateProfile().HandleAsync(
            new UpdateProfileCommand(Guid.NewGuid(), "Asha Rao", "asha@example.invalid", AuthFixture.Client), CancellationToken.None);

        revoked.Error.ShouldBe(AuthErrors.Unauthenticated);
        unknown.Error.ShouldBe(AuthErrors.Unauthenticated);
        _fx.Data.Owners.Single().DisplayName.ShouldBe("The Owner");
    }
}
