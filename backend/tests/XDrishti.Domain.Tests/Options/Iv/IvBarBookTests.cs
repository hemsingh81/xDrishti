using XDrishti.Domain.Options.Iv;
using static XDrishti.Domain.Tests.Options.Iv.IvKit;

namespace XDrishti.Domain.Tests.Options.Iv;

/// <summary>P14-11 criterion 2 at the book: only a bar that has ended at or before the instant, on the instant's own India date, is ever returned.</summary>
public sealed class IvBarBookTests
{
    private static IvBarBook BookOf(params DateTimeOffset[] starts)
    {
        var book = new IvBarBook();
        book.Add(1, "CE", 5, starts.Select(s => Bar(s, 0, 15f)));
        return book;
    }

    [Fact]
    public void The_last_bar_that_has_ended_by_the_instant_is_returned_and_one_that_ends_a_moment_later_is_not()
    {
        var book = BookOf(At(10, 50), At(10, 55), At(11, 0));

        book.Last(1, "CE", At(11, 0))!.Ts.ShouldBe(At(10, 55), "the 10:55 bar ends at 11:00, the 11:00 bar ends at 11:05");
        book.Last(1, "CE", At(11, 0).AddSeconds(-1))!.Ts.ShouldBe(At(10, 50));
        book.Last(1, "CE", At(11, 5))!.Ts.ShouldBe(At(11, 0));
    }

    [Fact]
    public void A_bar_of_an_earlier_session_is_never_carried_forward()
    {
        var book = BookOf(At(15, 25, new DateOnly(2026, 7, 28)));

        book.Last(1, "CE", At(9, 20)).ShouldBeNull();
    }

    [Fact]
    public void An_unknown_series_or_side_has_no_bar_and_an_empty_book_has_none()
    {
        var book = BookOf(At(10, 50));

        book.Last(2, "CE", At(11, 0)).ShouldBeNull();
        book.Last(1, "PE", At(11, 0)).ShouldBeNull();
        new IvBarBook().Last(1, "CE", At(11, 0)).ShouldBeNull();
    }

    [Fact]
    public void The_bars_of_one_timestamp_are_one_slice_by_offset()
    {
        var book = new IvBarBook();
        book.Add(1, "CE", 5, Offsets(At(10, 50), 15f, 100));

        var slice = book.Last(1, "CE", At(11, 0))!;

        slice.At(0)!.Iv.ShouldBe(15f);
        slice.At(3).ShouldNotBeNull();
        slice.At(4).ShouldBeNull();
    }
}
