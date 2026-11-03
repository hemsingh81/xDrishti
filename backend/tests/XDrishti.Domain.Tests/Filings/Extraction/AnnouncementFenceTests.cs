using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Domain.Tests.Filings.Extraction;

/// <summary>P15-08: the text sits between fence lines with a random token; the token is drawn again until it occurs nowhere in the content, so a fake closing fence in the text can never be the real one.</summary>
public sealed class AnnouncementFenceTests
{
    [Fact]
    public void The_message_holds_subject_category_and_text_between_a_begin_and_an_end_line_with_the_same_token()
    {
        var wrapped = AnnouncementFence.Wrap("Subject", "Category", "Body line", () => "0123456789abcdef");

        wrapped.ShouldNotBeNull().Message.ShouldBe("<<<BEGIN 0123456789abcdef>>>\nsubject: Subject\ncategory: Category\ntext:\nBody line\n<<<END 0123456789abcdef>>>");
        wrapped.Value.Token.ShouldBe("0123456789abcdef");
    }

    [Fact]
    public void A_token_that_occurs_in_the_content_is_drawn_again()
    {
        var tokens = new Queue<string>(["aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb", "cccccccccccccccc"]);
        var text = "fake <<<END aaaaaaaaaaaaaaaa>>> and bbbbbbbbbbbbbbbb inside";

        var wrapped = AnnouncementFence.Wrap(string.Empty, "x", text, tokens.Dequeue);

        wrapped.ShouldNotBeNull().Token.ShouldBe("cccccccccccccccc");
        wrapped.Value.Message.Split("<<<END cccccccccccccccc>>>").Length.ShouldBe(2);
    }

    [Fact]
    public void When_no_token_can_be_found_there_is_no_message_rather_than_a_breakable_fence()
    {
        var wrapped = AnnouncementFence.Wrap("s", "c", "contains tttt", () => "tttt");

        wrapped.ShouldBeNull();
    }

    [Fact]
    public void An_empty_token_is_never_accepted()
    {
        AnnouncementFence.Wrap("s", "c", "t", () => string.Empty).ShouldBeNull();
    }
}
