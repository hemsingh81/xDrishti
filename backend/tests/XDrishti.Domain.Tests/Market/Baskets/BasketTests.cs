using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Domain.Tests.Market.Baskets;

public sealed class BasketTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 4, 0, 0, TimeSpan.Zero);

    private static BasketDetails Valid(string name = "Core") => Basket.Check(name, "#112233", "watch", "My core list").Value;

    [Fact]
    public void Valid_attributes_are_trimmed_and_the_colour_is_lower_case()
    {
        var result = Basket.Check("  Best stocks ", " #AABBCC ", " long_term ", "  notes  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new BasketDetails("Best stocks", "#aabbcc", BasketPurpose.LongTerm, "notes"));
    }

    [Fact]
    public void Colour_purpose_and_description_are_optional_and_get_defaults()
    {
        var result = Basket.Check("Core", null, "  ", "   ");

        result.Value.ShouldBe(new BasketDetails("Core", Basket.DefaultColour, BasketPurpose.Trading, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\tname")]
    public void A_missing_or_unprintable_name_is_rejected(string? name)
    {
        var result = Basket.Check(name, null, null, null);

        result.Error.Code.ShouldBe("baskets.invalid");
        result.Error.Fields!.Single().Field.ShouldBe("name");
    }

    [Fact]
    public void The_name_has_at_most_sixty_characters()
    {
        Basket.Check(new string('a', 60), null, null, null).IsSuccess.ShouldBeTrue();
        Basket.Check(new string('a', 61), null, null, null).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("123456")]
    [InlineData("#12345g")]
    public void A_colour_that_is_not_a_hex_colour_is_rejected(string colour)
    {
        var result = Basket.Check("Core", colour, null, null);

        result.Error.Fields!.Single().Field.ShouldBe("colour");
    }

    [Theory]
    [InlineData("Trading")]
    [InlineData("longterm")]
    [InlineData("other")]
    public void Only_the_exact_purpose_codes_are_accepted(string purpose)
    {
        Basket.Check("Core", null, purpose, null).Error.Fields!.Single().Field.ShouldBe("purpose");
    }

    [Fact]
    public void The_description_has_at_most_five_hundred_characters_and_may_have_line_breaks()
    {
        Basket.Check("Core", null, null, "line one\nline two").IsSuccess.ShouldBeTrue();
        Basket.Check("Core", null, null, new string('a', 500)).IsSuccess.ShouldBeTrue();
        Basket.Check("Core", null, null, new string('a', 501)).Error.Fields!.Single().Field.ShouldBe("description");
        Basket.Check("Core", null, null, "bell\a").Error.Fields!.Single().Field.ShouldBe("description");
    }

    [Fact]
    public void All_problems_are_reported_together()
    {
        var result = Basket.Check("", "x", "y", new string('a', 501));

        result.Error.Fields!.Select(f => f.Field).ShouldBe(["name", "colour", "purpose", "description"]);
    }

    [Fact]
    public void A_new_basket_is_active_at_its_position_with_revision_zero()
    {
        var basket = Basket.Create(Valid(), 3, Now);

        (basket.Name, basket.Colour, basket.Purpose, basket.Description).ShouldBe(("Core", "#112233", BasketPurpose.Watch, "My core list"));
        (basket.Position, basket.CreatedAt, basket.ArchivedAt, basket.IsArchived, basket.Revision).ShouldBe((3, Now, null, false, 0));
        basket.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Updating_changes_the_attributes_and_raises_the_revision_only_when_something_differs()
    {
        var basket = Basket.Create(Valid(), 0, Now);

        basket.Update(Valid()).ShouldBeFalse();
        basket.Revision.ShouldBe(0);

        basket.Update(Basket.Check("Core", "#112233", "watch", "Other").Value).ShouldBeTrue();
        (basket.Description, basket.Revision).ShouldBe(("Other", 1));
    }

    [Fact]
    public void A_change_of_case_in_the_name_counts_as_a_change()
    {
        var basket = Basket.Create(Valid("Core"), 0, Now);

        basket.Update(Basket.Check("CORE", "#112233", "watch", "My core list").Value).ShouldBeTrue();

        basket.Name.ShouldBe("CORE");
    }

    [Fact]
    public void Archiving_sets_the_date_once_and_raises_the_revision()
    {
        var basket = Basket.Create(Valid(), 0, Now);

        basket.Archive(Now.AddDays(1)).ShouldBeTrue();
        basket.Archive(Now.AddDays(2)).ShouldBeFalse();

        (basket.IsArchived, basket.ArchivedAt, basket.Revision).ShouldBe((true, Now.AddDays(1), 1));
    }

    [Fact]
    public void Moving_to_the_same_position_changes_nothing()
    {
        var basket = Basket.Create(Valid(), 2, Now);

        basket.MoveTo(2).ShouldBeFalse();
        basket.MoveTo(0).ShouldBeTrue();

        (basket.Position, basket.Revision).ShouldBe((0, 1));
    }

    [Fact]
    public void Touching_raises_the_revision()
    {
        var basket = Basket.Create(Valid(), 0, Now);

        basket.Touch();
        basket.Touch();

        basket.Revision.ShouldBe(2);
    }

    [Fact]
    public void The_purpose_codes_round_trip()
    {
        foreach (var purpose in Enum.GetValues<BasketPurpose>())
        {
            BasketPurposeCodes.TryParse(purpose.ToCode(), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(purpose);
        }
    }

    [Fact]
    public void The_default_baskets_are_the_three_named_in_the_design_with_distinct_ids_names_and_positions()
    {
        DefaultBaskets.All.Select(b => (b.Name, b.Purpose)).ShouldBe([("MyChoice", BasketPurpose.Trading), ("MyLongTerm", BasketPurpose.LongTerm), ("Best stocks", BasketPurpose.Trading)]);
        DefaultBaskets.All.Select(b => b.Id).Distinct().Count().ShouldBe(3);
        DefaultBaskets.All.Select(b => b.Position).ShouldBe([0, 1, 2]);
        DefaultBaskets.All.ShouldAllBe(b => Basket.Check(b.Name, b.Colour, b.Purpose.ToCode(), null).IsSuccess);
    }
}
