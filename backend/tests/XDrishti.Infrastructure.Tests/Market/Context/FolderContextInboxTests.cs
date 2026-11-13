using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Market.Bars;
using XDrishti.Infrastructure.Market.Context;

namespace XDrishti.Infrastructure.Tests.Market.Context;

/// <summary>P3-01d fallback D: the drop folder lists top-level csv files, reads strict UTF-8, and moves handled files to done/ or failed/ without overwriting.</summary>
public sealed class FolderContextInboxTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "xd-ctx-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FolderContextInboxTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private FolderContextInbox Inbox(string? path = null) =>
        new(Options.Create(new DataOptions { Context = new ContextDataOptions { InboxPath = path ?? _folder } }), new FakeTimeProvider(new DateTimeOffset(2026, 3, 9, 10, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Only_top_level_csv_files_are_listed_in_name_order_and_a_missing_folder_is_empty()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "b.csv"), "x", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "a.csv"), "x", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "notes.txt"), "x", Ct);
        Directory.CreateDirectory(Path.Combine(_folder, "done"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "done", "old.csv"), "x", Ct);

        (await Inbox().ListAsync(Ct)).ShouldBe(["a.csv", "b.csv"]);
        (await Inbox(Path.Combine(_folder, "missing")).ListAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_is_read_and_a_non_utf8_file_is_unreadable()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "ok.csv"), "Date,Close\n", Ct);
        await File.WriteAllBytesAsync(Path.Combine(_folder, "bad.csv"), [0xFF, 0xFE, 0xFA], Ct);

        (await Inbox().ReadAsync("ok.csv", Ct)).ShouldBe("Date,Close\n");
        (await Inbox().ReadAsync("bad.csv", Ct)).ShouldBeNull();
        (await Inbox().ReadAsync("gone.csv", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_handled_file_moves_to_done_or_failed_with_a_time_prefix()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "a.csv"), "x", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "b.csv"), "x", Ct);

        await Inbox().MoveAsync("a.csv", processed: true, Ct);
        await Inbox().MoveAsync("b.csv", processed: false, Ct);

        File.Exists(Path.Combine(_folder, "done", "20260309T100000_a.csv")).ShouldBeTrue();
        File.Exists(Path.Combine(_folder, "failed", "20260309T100000_b.csv")).ShouldBeTrue();
        (await Inbox().ListAsync(Ct)).ShouldBeEmpty();
    }
}
