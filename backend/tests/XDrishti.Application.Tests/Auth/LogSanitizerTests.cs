using XDrishti.Application.Auth;

namespace XDrishti.Application.Tests.Auth;

public sealed class LogSanitizerTests
{
    [Fact]
    public void A_normal_email_is_logged_as_is() =>
        LogSanitizer.Email("owner@example.invalid").ShouldBe("owner@example.invalid");

    [Fact]
    public void Newlines_and_other_control_characters_cannot_forge_log_lines()
    {
        var result = LogSanitizer.Email("owner@example.invalid\r\n2026-10-10 INFO Login succeeded for admin\t\0");

        result.ShouldNotContain('\n');
        result.ShouldNotContain('\r');
        result.ShouldNotContain('\t');
        result.ShouldNotContain('\0');
        result.ShouldStartWith("owner@example.invalid");
    }

    [Fact]
    public void Unicode_line_separators_are_removed() =>
        LogSanitizer.Email("a@b.invalid\u2028forged\u2029line").ShouldBe("a@b.invalidforgedline");

    [Fact]
    public void An_over_long_value_is_cut_to_64_characters()
    {
        var result = LogSanitizer.Email("a@" + new string('x', 500));

        result.Length.ShouldBe(LogSanitizer.MaxLength);
        result.ShouldStartWith("a@xxx");
    }

    [Theory]
    [InlineData("hunter2-typed-into-the-wrong-field")]
    [InlineData("no at sign\r\nhere")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_value_that_does_not_look_like_an_email_is_replaced_by_a_placeholder(string? value) =>
        LogSanitizer.Email(value).ShouldBe(LogSanitizer.NotAnEmail);

    [Fact]
    public void An_at_sign_beyond_the_length_limit_does_not_count()
    {
        var typedPassword = new string('p', 100) + "@";

        LogSanitizer.Email(typedPassword).ShouldBe(LogSanitizer.NotAnEmail);
    }

    [Fact]
    public void A_value_that_only_has_control_characters_is_a_placeholder() =>
        LogSanitizer.Email("\r\n\t").ShouldBe(LogSanitizer.NotAnEmail);
}
