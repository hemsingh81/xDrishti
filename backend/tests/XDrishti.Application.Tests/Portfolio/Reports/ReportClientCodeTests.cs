using System.Text.Json;
using XDrishti.Application.Portfolio.Reports;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportFiles;
using static XDrishti.Application.Tests.Portfolio.Reports.ReportScenario;

namespace XDrishti.Application.Tests.Portfolio.Reports;

/// <summary>
/// The client code (UCC) of a report is not the Dhan client id of the account (P2-17 criterion 12, ADR 0029 section 8): the first report imported with a code leaves a salted hash of it on the account, later reports are compared with that hash only, an account that
/// remembers none and a report without a code never warn, a preview remembers nothing, and the code itself is never stored, reported, audited or logged.
/// </summary>
public sealed class ReportClientCodeTests
{
    private const string First = "AB12CD34EF";

    private const string Second = "ZZ99YY88XX";

    private const string Name = "ZENITH ENERGY SYSTEM LTD";

    private static Task<ReportScenario> StartAsync() => ReportScenario.StartAsync(s =>
    {
        s.Hold("ZENITHE", Zenith, 30, 12m);
        s.ListSampleCompanies();
    });

    /// <summary>A report of the kind with the code (null: no client line at all).</summary>
    private static string Report(string kind, string? code)
    {
        var text = kind switch
        {
            "bill" => Gtr([GtrRow("07 Apr 2026 00:00:00", Name, "NSE", "1001", 30, "360.00", 0, "0.00")]),
            "dp" => Dp([DpLine(1, "04-Jun-2026", "INE245A01021", "THE EXAMPLE POWER COMPANY LIMITED", "11.000", "Sell", "Other Fees", "12.5")]),
            _ => Demat([DematRow("ZENITH ENERGY SYSTEMS LIMITED", Zenith, free: "30")]),
        };
        return code is null
            ? text.Replace($"UCC,{ClientCode}\n", string.Empty, StringComparison.Ordinal).Replace($",UCC,{ClientCode}", string.Empty, StringComparison.Ordinal)
            : text.Replace(ClientCode, code, StringComparison.Ordinal);
    }

    public static TheoryData<string> Kinds => new() { "bill", "dp", "demat" };

    [Fact]
    public void The_hash_is_the_same_for_the_same_code_whatever_its_case_and_edge_spaces_and_is_not_the_code()
    {
        var hash = ReportClientCodes.Hash(First);

        ReportClientCodes.Hash("  ab12cd34ef ").ShouldBe(hash);
        ReportClientCodes.Hash(Second).ShouldNotBe(hash);
        hash.Length.ShouldBe(64);
        hash.ShouldAllBe(c => char.IsAsciiHexDigitLower(c));
        hash.ShouldNotContain(First, Case.Insensitive);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_account_that_remembers_no_code_never_warns_and_the_first_import_remembers_the_hash(string kind)
    {
        var s = await StartAsync();

        var preview = await s.PreviewOkAsync(Report(kind, First));
        var done = Ok(await s.ImportAsync(Report(kind, First), rebuild: false));

        preview.Warnings.ShouldNotContain(w => w.Code == "other_account");
        done.Status.ShouldNotBe("rejected");
        s.RememberedHash().ShouldBe(ReportClientCodes.Hash(First));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_preview_remembers_nothing(string kind)
    {
        var s = await StartAsync();

        await s.PreviewOkAsync(Report(kind, First));

        s.RememberedHash().ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_later_report_for_another_code_warns_and_is_refused_unless_ticked_and_the_remembered_hash_stays(string kind)
    {
        var s = await StartAsync();
        Ok(await s.ImportAsync(Report(kind, First), rebuild: false));
        var remembered = s.RememberedHash();

        var preview = await s.PreviewOkAsync(Report(kind, Second));
        var refused = Ok(await s.ImportAsync(Report(kind, Second), rebuild: false));
        var ticked = Ok(await s.ImportAsync(Report(kind, Second), rebuild: false, acceptOtherAccount: true));
        var again = await s.PreviewOkAsync(Report(kind, "  " + First.ToLowerInvariant() + " "));

        preview.Warnings.ShouldContain(w => w.Code == "other_account");
        (refused.Status, refused.Error!.Code).ShouldBe(("rejected", "portfolio.other_account"));
        ticked.Status.ShouldNotBe("rejected");
        again.Warnings.ShouldNotContain(w => w.Code == "other_account");
        s.RememberedHash().ShouldBe(remembered);
        s.ClientCodes.Codes.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_ticked_report_for_another_code_is_the_first_one_imported_remembers_its_code(string kind)
    {
        var s = await StartAsync();

        Ok(await s.ImportAsync(Report(kind, Second), rebuild: false, acceptOtherAccount: true));

        s.RememberedHash().ShouldBe(ReportClientCodes.Hash(Second));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_report_without_a_client_code_never_warns_and_remembers_nothing(string kind)
    {
        var s = await StartAsync();
        var plain = Report(kind, null);
        plain.ShouldNotContain("UCC");

        var preview = await s.PreviewOkAsync(plain);
        var done = Ok(await s.ImportAsync(plain, rebuild: false));
        s.Remembers(First);
        var againPreview = await s.PreviewOkAsync(plain);
        var again = Ok(await s.ImportAsync(plain, rebuild: false));

        preview.Warnings.ShouldNotContain(w => w.Code == "other_account");
        againPreview.Warnings.ShouldNotContain(w => w.Code == "other_account");
        done.Status.ShouldNotBe("rejected");
        again.Status.ShouldNotBe("rejected");
        s.RememberedHash().ShouldBe(ReportClientCodes.Hash(First));
        s.ClientCodes.Codes.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("dp")]
    [InlineData("demat")]
    public async Task An_import_that_is_refused_remembers_nothing(string kind)
    {
        var s = await StartAsync();

        var stale = Ok(await s.ImportAsync(Report(kind, First), digest: "not-the-digest-of-the-preview"));

        stale.Error!.Code.ShouldBe("portfolio.preview_stale");
        s.RememberedHash().ShouldBeNull();
    }

    [Fact]
    public async Task The_report_code_is_not_compared_with_the_dhan_client_id_of_the_account()
    {
        var s = await StartAsync();
        s.Account.ClientId.ShouldNotBe(First);

        var strange = await s.PreviewOkAsync(Report("bill", First));
        s.Remembers(First);
        var sameAsClientId = await s.PreviewOkAsync(Report("bill", s.Account.ClientId));
        var sameAsRemembered = await s.PreviewOkAsync(Report("bill", First));

        strange.Warnings.ShouldNotContain(w => w.Code == "other_account");
        sameAsClientId.Warnings.ShouldContain(w => w.Code == "other_account");
        sameAsRemembered.Warnings.ShouldNotContain(w => w.Code == "other_account");
    }

    [Fact]
    public async Task The_remembered_code_is_per_account()
    {
        var s = await StartAsync();
        s.Remembers(First);
        var other = await s.Fx.ConnectedAsync("Example Two", clientId: "2000000002");

        var preview = await s.PreviewAsync(Report("bill", Second), accountId: other.Id);

        preview.IsSuccess.ShouldBeTrue();
        preview.Value.Warnings.ShouldNotContain(w => w.Code == "other_account");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_code_itself_is_never_stored_reported_audited_or_logged(string kind)
    {
        var s = await StartAsync();
        var preview = await s.PreviewOkAsync(Report(kind, First));
        var first = Ok(await s.ImportAsync(Report(kind, First), rebuild: false));
        var refused = Ok(await s.ImportAsync(Report(kind, Second), rebuild: false));
        var ticked = Ok(await s.ImportAsync(Report(kind, Second), rebuild: false, acceptOtherAccount: true));

        var disclosed = s.Disclosed(preview, first, refused, ticked);
        var stored = JsonSerializer.Serialize(s.ClientCodes.Codes);

        foreach (var code in new[] { First, Second })
        {
            disclosed.ShouldNotContain(code, Case.Insensitive);
            stored.ShouldNotContain(code, Case.Insensitive);
        }

        disclosed.ShouldNotContain("SENTINEL");
        s.ClientCodes.Codes.Single().Hash.ShouldBe(ReportClientCodes.Hash(First));
        s.PreviewLog.Messages.ShouldNotBeEmpty();
        s.ImportLog.Messages.ShouldNotBeEmpty();
    }
}
