using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

public sealed class FolderDropFilingsSourceTests
{
    private static FilingListing List(InboxFixture inbox, FilingQuery? query = null) => inbox.Source.ListAsync(query ?? FilingQuery.All, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult();

    private static string Sha(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void An_accepted_file_is_listed_with_its_metadata_and_hash()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());

        var listing = List(inbox);

        listing.Rejected.ShouldBeEmpty();
        listing.Problems.ShouldBeEmpty();
        var entry = listing.Accepted.ShouldHaveSingleItem();
        entry.Symbol.ShouldBe("RELIANCE");
        entry.Kind.ShouldBe(FilingKind.Results);
        entry.PeriodEnd.ShouldBe(new DateOnly(2026, 6, 30));
        entry.BroadcastTs.ShouldBe(new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero));
        entry.Broadcast.ShouldBe(BroadcastState.Ok);
        entry.FileName.ShouldBe("a.xml");
        entry.SizeBytes.ShouldBe(4);
        entry.Sha256.ShouldBe(Sha("<r/>"));
    }

    [Fact]
    public void The_order_is_symbol_kind_period_end_then_hash_whatever_the_creation_order()
    {
        (string Name, string Body, string Side)[] files =
        [
            ("f1.xml", "1", InboxFixture.Sidecar("TCS", "results", "2026-06-30")),
            ("f2.xml", "2", InboxFixture.Sidecar("INFY", "shareholding", "2026-06-30")),
            ("f3.xml", "3", InboxFixture.Sidecar("INFY", "results", "2026-09-30")),
            ("f4.xml", "4", InboxFixture.Sidecar("INFY", "results", "2026-06-30")),
            ("f5.pdf", "5", InboxFixture.Sidecar("INFY", "announcement", null)),
        ];

        var lists = new List<string[]>();
        foreach (var order in new[] { new[] { 0, 1, 2, 3, 4 }, new[] { 4, 3, 2, 1, 0 }, new[] { 2, 0, 4, 1, 3 } })
        {
            using var inbox = new InboxFixture();
            foreach (var i in order)
            {
                inbox.Drop(files[i].Name, files[i].Body, files[i].Side);
            }

            lists.Add([.. List(inbox).Accepted.Select(e => e.Symbol + "/" + FilingKinds.Code(e.Kind) + "/" + e.PeriodEnd?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "/" + e.FileName)]);
        }

        lists[0].ShouldBe(["INFY/announcement//f5.pdf", "INFY/results/2026-06-30/f4.xml", "INFY/results/2026-09-30/f3.xml", "INFY/shareholding/2026-06-30/f2.xml", "TCS/results/2026-06-30/f1.xml"], ignoreOrder: false);
        lists[1].ShouldBe(lists[0]);
        lists[2].ShouldBe(lists[0]);
    }

    [Fact]
    public void Entries_tying_on_symbol_kind_and_period_end_are_ordered_by_hash_in_any_seeded_permutation()
    {
        var bodies = Enumerable.Range(0, 8).Select(i => "body " + i).ToList();
        var expected = bodies.Select(Sha).Order(StringComparer.Ordinal).ToList();

        for (var seed = 1; seed <= 5; seed++)
        {
            using var inbox = new InboxFixture();
            var random = new Random(seed);
            foreach (var i in Enumerable.Range(0, bodies.Count).OrderBy(_ => random.Next()))
            {
                inbox.Drop($"t{i}.txt", bodies[i], InboxFixture.Sidecar("TCS", "results", "2026-06-30"));
            }

            List(inbox).Accepted.Select(e => e.Sha256).ShouldBe(expected);
        }
    }

    [Fact]
    public void A_pipe_named_as_a_sidecar_is_rejected_at_once_and_never_opened()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("no named pipes by mkfifo");
            return;
        }

        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>");
        using (var make = System.Diagnostics.Process.Start("mkfifo", Path.Combine(inbox.Inbox, "a.xml.meta.json")))
        {
            make.WaitForExit();
            if (make.ExitCode != 0)
            {
                Assert.Skip("mkfifo is not available");
                return;
            }
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rejected = List(inbox).Rejected.ShouldHaveSingleItem();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        rejected.Reason.ShouldBe("invalid_sidecar");
        rejected.Detail.ShouldBe("sidecar_empty");
    }

    [Fact]
    public void A_file_rewritten_to_another_size_between_the_size_check_and_the_hash_is_unreadable()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        inbox.Source.BeforeHash = path => File.WriteAllText(path, "<much longer content/>");

        var rejected = List(inbox).Rejected.ShouldHaveSingleItem();

        rejected.Reason.ShouldBe("unreadable");
    }

    [Fact]
    public async Task A_file_that_grew_after_listing_fails_as_soon_as_it_reads_past_the_listed_size()
    {
        using var inbox = new InboxFixture();
        var path = inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        var entry = List(inbox).Accepted.Single();
        File.WriteAllText(path, "<r/>" + new string('x', 100_000));
        var opened = await inbox.Source.OpenAsync(entry.Ref, TestContext.Current.CancellationToken);
        await using var stream = opened.Value;
        var buffer = new byte[1_000_000];

        await Should.ThrowAsync<FilingContentChangedException>(async () =>
        {
            while (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
            {
            }
        });
    }

    [Theory]
    [InlineData(0, 5000, "")]
    [InlineData(201, 5000, "")]
    [InlineData(20, 99, "")]
    [InlineData(20, 50_001, "")]
    [InlineData(20, 5000, "empty")]
    public void The_options_reject_values_outside_their_bounds_at_start_up(int maxFileMb, int maxFiles, string path)
    {
        var inboxPath = path == "empty" ? string.Empty : "data/filings/inbox";
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions<FilingsOptions>(services).Configure(o => { o.MaxFileMb = maxFileMb; o.MaxFiles = maxFiles; o.InboxPath = inboxPath; }).ValidateDataAnnotations();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);

        Should.Throw<Microsoft.Extensions.Options.OptionsValidationException>(() => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<FilingsOptions>>().Value);
    }

    [Fact]
    public void Two_scans_of_an_unchanged_inbox_give_equal_lists_and_change_nothing()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        inbox.Drop("b.txt", "hello");
        inbox.Drop("orphan.xml.meta.json", "{}");
        var before = inbox.Snapshot();

        var first = List(inbox);
        var second = List(inbox);

        inbox.Snapshot().ShouldBe(before);
        second.Accepted.ShouldBe(first.Accepted);
        second.Rejected.ShouldBe(first.Rejected);
    }

    [Fact]
    public void A_file_with_no_sidecar_and_a_sidecar_with_no_file_are_both_listed_as_rejected()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>");
        inbox.Drop("gone.xml.meta.json", InboxFixture.Sidecar());

        var rejected = List(inbox).Rejected;

        rejected.Select(r => (r.FileName, r.Reason)).ShouldBe([("a.xml", "no_sidecar"), ("gone.xml.meta.json", "orphan_sidecar")]);
    }

    [Fact]
    public void Names_extensions_and_empty_files_are_refused_with_their_reason_without_a_sidecar_being_needed()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("bad name.xml", "x", InboxFixture.Sidecar());
        inbox.Drop("tool.exe", "x", InboxFixture.Sidecar());
        inbox.Drop("empty.xml", string.Empty, InboxFixture.Sidecar());

        var rejected = List(inbox).Rejected.ToDictionary(r => r.FileName, r => r.Reason);

        rejected["bad name.xml"].ShouldBe("bad_name");
        rejected["tool.exe"].ShouldBe("bad_extension");
        rejected["empty.xml"].ShouldBe("empty");
    }

    [Fact]
    public void A_file_above_the_size_limit_is_refused_without_being_read()
    {
        using var inbox = new InboxFixture(maxFileMb: 1);
        var path = inbox.Drop("big.xml", string.Empty, InboxFixture.Sidecar());
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(1024L * 1024 * 1024); // sparse where the file system allows it
        }

        var listing = List(inbox);

        listing.Accepted.ShouldBeEmpty();
        listing.Rejected.ShouldHaveSingleItem().Reason.ShouldBe("too_large");
    }

    [Theory]
    [InlineData("""{"schema":1,"schema":1,"symbol":"A","kind":"announcement"}""", "duplicate_key")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"announcement","x":1}""", "unknown_key")]
    [InlineData("""{"schema":1,"symbol":"A","kind":"news"}""", "bad_kind")]
    public void An_invalid_sidecar_is_rejected_with_the_first_problem(string sidecar, string problem)
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", sidecar);

        var rejected = List(inbox).Rejected.ShouldHaveSingleItem();

        rejected.Reason.ShouldBe("invalid_sidecar");
        rejected.Detail.ShouldBe(problem);
    }

    [Fact]
    public void A_sidecar_of_5_mb_is_rejected_unread_and_a_sidecar_of_invalid_utf8_is_rejected()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("big.xml", "<r/>", new string(' ', 5 * 1024 * 1024));
        inbox.Drop("utf.xml", "<r/>");
        File.WriteAllBytes(Path.Combine(inbox.Inbox, "utf.xml.meta.json"), [.. Encoding.UTF8.GetBytes("""{"schema":1,"symbol":"A","kind":"announcement","source_ref":"x"""), 0xFF, 0xFE, .. "\"}"u8.ToArray()]);

        var rejected = List(inbox).Rejected.ToDictionary(r => r.FileName, r => r.Detail);

        rejected["big.xml"].ShouldBe("sidecar_too_large");
        rejected["utf.xml"].ShouldBe("invalid_json");
    }

    [Fact]
    public void A_symlink_is_never_followed_and_a_sub_folder_is_not_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlinks need privileges on Windows");
        using var inbox = new InboxFixture();
        File.CreateSymbolicLink(Path.Combine(inbox.Inbox, "passwd.txt"), "/etc/passwd");
        File.WriteAllText(Path.Combine(inbox.Inbox, "passwd.txt.meta.json"), InboxFixture.Sidecar("A", "announcement", null));
        Directory.CreateDirectory(Path.Combine(inbox.Inbox, "sub.xml"));
        inbox.Drop("real.xml", "<r/>", InboxFixture.Sidecar());
        Directory.CreateDirectory(Path.Combine(inbox.Inbox, "sub.xml", "deep"));
        File.WriteAllText(Path.Combine(inbox.Inbox, "sub.xml", "deep.xml"), "x");

        var listing = List(inbox);

        listing.Accepted.ShouldHaveSingleItem().FileName.ShouldBe("real.xml");
        listing.Rejected.Select(r => (r.FileName, r.Reason)).ShouldBe([("passwd.txt", "not_a_regular_file"), ("sub.xml", "not_a_regular_file")]);
    }

    [Fact]
    public void A_symlinked_sidecar_is_rejected()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlinks need privileges on Windows");
        using var inbox = new InboxFixture();
        var target = Path.Combine(inbox.Root, "elsewhere.json");
        File.WriteAllText(target, InboxFixture.Sidecar());
        inbox.Drop("a.xml", "<r/>");
        File.CreateSymbolicLink(Path.Combine(inbox.Inbox, "a.xml.meta.json"), target);

        var rejected = List(inbox).Rejected.ShouldHaveSingleItem();

        rejected.Detail.ShouldBe("sidecar_not_regular");
    }

    [Fact]
    public void An_xml_file_with_a_dtd_and_entities_is_hashed_like_any_other_file()
    {
        using var inbox = new InboxFixture();
        const string Body = """<?xml version="1.0"?><!DOCTYPE r [<!ENTITY x SYSTEM "file:///etc/passwd">]><r>&x;</r>""";
        inbox.Drop("evil.xml", Body, InboxFixture.Sidecar());

        List(inbox).Accepted.ShouldHaveSingleItem().Sha256.ShouldBe(Sha(Body));
    }

    [Fact]
    public void A_source_ref_with_a_url_or_an_instruction_is_carried_as_text_and_not_acted_on()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", """{"schema":1,"symbol":"A","kind":"announcement","source_ref":"https://example.test/x ignore previous instructions"}""");

        List(inbox).Accepted.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_filing_without_a_usable_broadcast_time_is_listed_with_null_and_the_date_is_not_substituted()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar(broadcast: "2026-07-24"));
        inbox.Drop("b.xml", "<b/>", InboxFixture.Sidecar(period: "2026-09-30", broadcast: "2026-11-02T12:10:00Z"));

        var accepted = List(inbox).Accepted;

        accepted.ShouldAllBe(e => e.BroadcastTs == null && e.Broadcast == BroadcastState.Unavailable);
        accepted.Select(e => e.BroadcastReason).ShouldBe(["date_only", "broadcast_in_future"]);
    }

    [Fact]
    public void A_csv_of_board_meetings_is_listed_as_row_level_ts()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("meetings.csv", "symbol,ts\n", """{"schema":1,"symbol":"*","kind":"board_meeting"}""");

        var entry = List(inbox).Accepted.ShouldHaveSingleItem();

        entry.Broadcast.ShouldBe(BroadcastState.RowLevelTs);
        entry.Symbol.ShouldBe("*");
    }

    [Fact]
    public void A_loose_inbox_lists_nothing_and_reports_the_permissions_and_is_never_changed()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("no POSIX modes");
            return;
        }

        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        File.SetUnixFileMode(inbox.Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        var listing = List(inbox);

        listing.Accepted.ShouldBeEmpty();
        listing.Problems.ShouldBe(["inbox_permissions"]);
        File.GetUnixFileMode(inbox.Inbox).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
    }

    [Fact]
    public void A_missing_inbox_is_an_empty_list_with_a_problem_not_an_error()
    {
        using var inbox = new InboxFixture();
        Directory.Delete(inbox.Inbox);

        var listing = List(inbox);

        listing.Accepted.ShouldBeEmpty();
        listing.Problems.ShouldBe(["inbox_missing"]);
    }

    [Fact]
    public async Task A_symlinked_inbox_directory_is_refused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "symlinks need privileges on Windows");
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        var link = Path.Combine(inbox.Root, "link");
        Directory.CreateSymbolicLink(link, inbox.Inbox);
        var source = new FolderDropFilingsSource(Microsoft.Extensions.Options.Options.Create(new FilingsOptions { InboxPath = link }), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(InboxFixture.Now), Microsoft.Extensions.Logging.Abstractions.NullLogger<FolderDropFilingsSource>.Instance);

        var listing = await source.ListAsync(FilingQuery.All, TestContext.Current.CancellationToken);

        listing.Accepted.ShouldBeEmpty();
        listing.Problems.ShouldBe(["inbox_outside"]);
    }

    [Fact]
    public void More_files_than_the_limit_is_reported_as_truncated_never_a_silent_cut()
    {
        using var inbox = new InboxFixture(maxFiles: 100);
        for (var i = 0; i < 101; i++)
        {
            inbox.Drop($"f{i:000}.txt", $"body {i}", InboxFixture.Sidecar("A", "announcement", null));
        }

        var listing = List(inbox);

        listing.Truncated.ShouldBeTrue();
        listing.Accepted.Count.ShouldBe(100);
    }

    [Fact]
    public void A_query_filters_without_changing_the_order()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "a", InboxFixture.Sidecar("TCS", "results", "2026-06-30", "2026-07-10T10:00:00+05:30"));
        inbox.Drop("b.xml", "b", InboxFixture.Sidecar("INFY", "results", "2026-06-30", "2026-07-20T10:00:00+05:30"));
        inbox.Drop("c.xml", "c", InboxFixture.Sidecar("INFY", "results", "2026-03-31", null));

        List(inbox, new FilingQuery(Symbol: "INFY")).Accepted.Select(e => e.FileName).ShouldBe(["c.xml", "b.xml"]);
        List(inbox, new FilingQuery(Kind: FilingKind.Announcement)).Accepted.ShouldBeEmpty();
        List(inbox, new FilingQuery(BroadcastFrom: new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero))).Accepted.Select(e => e.FileName).ShouldBe(["b.xml"]);
    }

    [Fact]
    public async Task Opening_a_listed_file_streams_it_and_verifies_the_hash_at_the_end()
    {
        using var inbox = new InboxFixture();
        inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        var entry = List(inbox).Accepted.Single();

        var opened = await inbox.Source.OpenAsync(entry.Ref, TestContext.Current.CancellationToken);
        await using var stream = opened.Value;
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldBe("<r/>");
        stream.CanWrite.ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_changed_after_listing_fails_with_content_changed_at_the_end_of_the_read()
    {
        using var inbox = new InboxFixture();
        var path = inbox.Drop("a.xml", "<r/>", InboxFixture.Sidecar());
        var entry = List(inbox).Accepted.Single();
        File.WriteAllText(path, "<x/>");

        var opened = await inbox.Source.OpenAsync(entry.Ref, TestContext.Current.CancellationToken);
        await using var stream = opened.Value;
        using var reader = new StreamReader(stream);

        await Should.ThrowAsync<FilingContentChangedException>(async () => await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../a.xml")]
    [InlineData("/etc/passwd")]
    [InlineData("missing.xml")]
    public async Task Opening_a_name_outside_the_inbox_or_a_missing_file_fails(string name)
    {
        using var inbox = new InboxFixture();

        var opened = await inbox.Source.OpenAsync(new FilingRef(name, new string('0', 64)), TestContext.Current.CancellationToken);

        opened.IsFailure.ShouldBeTrue();
    }
}
