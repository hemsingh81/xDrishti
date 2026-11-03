#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Eod;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Market.Eod;

/// <summary>CF-04 criterion 16 (unit part): the NSE actions file feeds the existing import PREVIEW only. Header names are TO BE VERIFIED BY THE PROBE.</summary>
public sealed class NseActionsTests
{
    private const string Header = "SYMBOL,COMPANY NAME,SERIES,PURPOSE,FACE VALUE,EX-DATE,RECORD DATE,BOOK CLOSURE START DATE,BOOK CLOSURE END DATE";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string File(params string[] lines) => Header + "\n" + string.Join('\n', lines) + "\n";

    [Theory]
    [InlineData("Face Value Split (Sub-Division) - From Rs 10/- Per Share To Re 1/- Per Share", 1, 10)]
    [InlineData("Face Value Split (Sub-Division) - From Rs 10/- Per Share To Rs 2/- Per Share", 1, 5)]
    [InlineData("Face Value Split From Rs.5/- To Rs.2/-", 2, 5)]
    public void A_split_reads_as_to_over_from_reduced(string purpose, int numerator, int denominator) =>
        NseActionsParser.Split(purpose).ShouldBe((numerator, denominator));

    [Theory]
    [InlineData("Face Value Split From Rs 10/- To Rs 2.5/-")]
    [InlineData("Face Value Split")]
    [InlineData("Face Value Split From Rs 10/- To Rs 10/-")]
    public void A_split_that_cannot_be_read_exactly_is_not_guessed(string purpose) => NseActionsParser.Split(purpose).ShouldBeNull();

    [Fact]
    public void A_bonus_reads_as_bonus_to_held() => NseActionsParser.Bonus("Bonus 1:1").ShouldBe((1, 1));

    [Fact]
    public async Task Splits_and_bonuses_become_import_lines_and_everything_else_is_counted()
    {
        var text = File(
            "ABC,ABC LTD,EQ,Face Value Split (Sub-Division) - From Rs 10/- Per Share To Rs 2/- Per Share,10,10-Mar-2023,,,",
            "DEF,DEF LTD,EQ,Bonus 1:1,10,01-Aug-2024,,,",
            "GHI,GHI LTD,EQ,Dividend - Rs 5 Per Share,10,01-Aug-2024,,,",
            "JKL,JKL LTD,EQ,Bonus,10,01-Aug-2024,,,");

        var parsed = await NseActionsParser.ParseAsync(text, Ct);

        parsed.Value.ShouldSatisfyAllConditions(
            p => p.Splits.ShouldBe(1),
            p => p.Bonuses.ShouldBe(1),
            p => p.Other.ShouldBe(1),
            p => p.Unreadable.ShouldBe(1),
            p => p.Csv.ShouldContain("ABC,2023-03-10,split,1,5,"),
            p => p.Csv.ShouldContain("DEF,2024-08-01,bonus,1,1,"));
    }

    [Fact]
    public async Task An_unknown_header_refuses_the_file()
    {
        var parsed = await NseActionsParser.ParseAsync("A,B,C\n1,2,3\n", Ct);

        parsed.Error.Code.ShouldBe("history.unknown_format");
    }

    [Fact]
    public async Task The_handler_only_ever_asks_the_import_for_a_preview()
    {
        var import = Substitute.For<ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult>>();
        import.HandleAsync(default!, Ct).ReturnsForAnyArgs(Result.Success(new ImportCorporateActionsResult(true, 1, 1, 0, 0, 0, [], [])));
        var handler = new PreviewNseActionsHandler(import);

        var result = await handler.HandleAsync(new PreviewNseActionsCommand(File("ABC,ABC LTD,EQ,Bonus 1:1,10,01-Aug-2024,,,")), Ct);

        result.IsSuccess.ShouldBeTrue();
        await import.Received(1).HandleAsync(Arg.Is<ImportCorporateActionsCommand>(c => c.Preview && !c.AcknowledgeCsvAdjusted), Ct);
        await import.DidNotReceive().HandleAsync(Arg.Is<ImportCorporateActionsCommand>(c => !c.Preview), Ct);
    }

    [Fact]
    public async Task A_file_without_splits_or_bonuses_never_reaches_the_import()
    {
        var import = Substitute.For<ICommandHandler<ImportCorporateActionsCommand, ImportCorporateActionsResult>>();
        var handler = new PreviewNseActionsHandler(import);

        var result = await handler.HandleAsync(new PreviewNseActionsCommand(File("GHI,GHI LTD,EQ,Dividend - Rs 5,10,01-Aug-2024,,,")), Ct);

        result.Value.Other.ShouldBe(1);
        await import.DidNotReceiveWithAnyArgs().HandleAsync(default!, Ct);
    }
}
