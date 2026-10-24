using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-10 (ADR 0095): the owner can only lower a size, for an account the ticket already has, and the stored text reads back the same.</summary>
public sealed class TicketModificationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static readonly (Guid, string, int)[] Allocated = [(A, "paper", 10), (B, "paper", 4)];

    [Fact]
    public void A_reduction_is_valid_and_round_trips_through_its_canonical_text()
    {
        var valid = TicketModification.Validate(Allocated, [new QuantityChange(B, 2), new QuantityChange(A, 7)]);

        valid.IsSuccess.ShouldBeTrue();
        var text = TicketModification.Serialize(valid.Value);
        text.ShouldBe("qty:00000000-0000-0000-0000-00000000000a=7;qty:00000000-0000-0000-0000-00000000000b=2");
        TicketModification.Parse(text).Value.ShouldBe([new QuantityChange(A, 7), new QuantityChange(B, 2)]);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_equal_higher_or_non_positive_quantity_is_refused(int quantity)
    {
        TicketModification.Validate(Allocated, [new QuantityChange(A, quantity)]).Error.Code.ShouldBe(TicketModification.InvalidCode);
    }

    [Fact]
    public void An_account_the_ticket_is_not_sized_for_a_repeated_account_and_an_empty_change_are_refused()
    {
        TicketModification.Validate(Allocated, [new QuantityChange(Guid.NewGuid(), 1)]).IsFailure.ShouldBeTrue();
        TicketModification.Validate(Allocated, [new QuantityChange(A, 5), new QuantityChange(A, 4)]).IsFailure.ShouldBeTrue();
        TicketModification.Validate(Allocated, []).IsFailure.ShouldBeTrue();
        TicketModification.Validate(Allocated, null).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_live_allocation_cannot_be_modified()
    {
        TicketModification.Validate([(A, "live", 10)], [new QuantityChange(A, 5)]).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qty:nope=1")]
    [InlineData("qty:00000000-0000-0000-0000-00000000000a=-3")]
    [InlineData("qty:00000000-0000-0000-0000-00000000000b=2;qty:00000000-0000-0000-0000-00000000000a=7")]
    [InlineData("stop:tighter")]
    public void A_stored_text_that_is_not_canonical_is_not_read(string? text)
    {
        TicketModification.Parse(text).IsFailure.ShouldBeTrue();
    }
}
