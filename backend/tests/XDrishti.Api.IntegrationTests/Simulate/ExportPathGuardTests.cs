using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criterion 11 of P3-04b1: the path guard. Every path is built from fixed names and sanitised parts and lies under <c>&lt;root&gt;/sim/</c>; nothing is created outside the root; a wrong root is a configuration error and the rest are refusals.</summary>
public sealed class ExportPathGuardTests
{
    private static ExportFile File(string name) => ExportFixture.Text(name, "x");

    // the root: a configuration error

    [Fact]
    public async Task A_relative_root_is_a_configuration_error()
    {
        var result = await ExportFixture.WriteAsync("exports/relative");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.RootInvalidCode);
    }

    [Fact]
    public async Task A_root_whose_parent_does_not_exist_is_a_configuration_error_and_nothing_is_created()
    {
        using var temp = new TempFolder();
        var before = ExportFixture.Tree(temp.Path);

        var result = await ExportFixture.WriteAsync(Path.Combine(temp.Path, "no-such-parent", "exports"));

        result.Error.Code.ShouldBe(ExportErrors.RootInvalidCode);
        ExportFixture.Tree(temp.Path).ShouldBe(before);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public async Task An_empty_blank_or_file_system_root_is_a_configuration_error(string root)
    {
        (await ExportFixture.WriteAsync(root)).Error.Code.ShouldBe(ExportErrors.RootInvalidCode);
    }

    [Fact]
    public async Task A_root_that_is_a_file_is_a_configuration_error()
    {
        using var temp = new TempFolder();
        System.IO.File.WriteAllText(temp.Root, "a file");

        (await ExportFixture.WriteAsync(temp.Root)).Error.Code.ShouldBe(ExportErrors.RootInvalidCode);
    }

    [Fact]
    public async Task A_null_root_is_a_configuration_error()
    {
        (await ExportFixture.WriteAsync(null!)).Error.Code.ShouldBe(ExportErrors.RootInvalidCode);
    }

    // links

    [Fact]
    public async Task A_sim_folder_that_is_a_symbolic_link_is_refused_and_nothing_is_written_through_it()
    {
        using var temp = new TempFolder();
        var target = Directory.CreateDirectory(Path.Combine(temp.Path, "elsewhere")).FullName;
        Directory.CreateDirectory(temp.Root);
        Directory.CreateSymbolicLink(Path.Combine(temp.Root, "sim"), target);

        var result = await ExportFixture.WriteAsync(temp.Root);

        result.Error.Code.ShouldBe(ExportErrors.LinkRefusedCode);
        Directory.EnumerateFileSystemEntries(target).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_run_folder_that_is_a_symbolic_link_is_refused_even_with_force(bool targetExists)
    {
        using var temp = new TempFolder();
        var target = Path.Combine(temp.Path, "elsewhere");
        if (targetExists)
        {
            Directory.CreateDirectory(target);
        }

        Directory.CreateDirectory(Path.Combine(temp.Root, "sim"));
        Directory.CreateSymbolicLink(Path.Combine(temp.Root, "sim", ExportFixture.RunId), target);

        foreach (var force in new[] { false, true })
        {
            var result = await ExportFixture.WriteAsync(temp.Root, force: force);

            result.Error.Code.ShouldBe(ExportErrors.LinkRefusedCode);
        }

        Directory.Exists(target).ShouldBe(targetExists);
        if (targetExists)
        {
            Directory.EnumerateFileSystemEntries(target).ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task A_sim_that_is_a_file_is_refused()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Root);
        System.IO.File.WriteAllText(Path.Combine(temp.Root, "sim"), "a file");

        (await ExportFixture.WriteAsync(temp.Root)).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
    }

    // names

    public static TheoryData<string> BadNames => new()
    {
        "..",
        "../escape.csv",
        "a/../b.csv",
        "a/b/c.csv",
        "/absolute.csv",
        "trailing/",
        "a\\b.csv",
        "c:name.csv",
        "a:b",
        "nul\0name",
        "tab\tname",
        "bell\u0007name",
        "bidi\u202Ename",
        "zero\u200Bwidth",
        "space name.csv",
        "é.csv",
        "CON",
        "con.txt",
        "NUL.csv",
        "cards/COM1.html",
        "manifest.json",
        "MANIFEST.JSON",
        "",
    };

    [Theory]
    [MemberData(nameof(BadNames))]
    public async Task A_file_name_that_could_leave_the_folder_or_confuse_the_file_system_is_refused_and_nothing_is_created(string name)
    {
        using var temp = new TempFolder();
        var before = ExportFixture.Tree(temp.Path);

        var result = await ExportFixture.WriteAsync(temp.Root, [File("ok.txt"), File(name)]);

        result.IsFailure.ShouldBeTrue("'" + name + "'");
        result.Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        ExportFixture.Tree(temp.Path).ShouldBe(before, "not even the root is created");
    }

    [Fact]
    public async Task A_very_long_name_is_refused()
    {
        using var temp = new TempFolder();

        (await ExportFixture.WriteAsync(temp.Root, [File(new string('a', 101))])).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        (await ExportFixture.WriteAsync(temp.Root, [File("cards/" + new string('a', 101))])).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
    }

    [Fact]
    public async Task Two_names_that_differ_only_in_case_or_a_file_that_is_also_a_folder_are_refused()
    {
        using var temp = new TempFolder();

        (await ExportFixture.WriteAsync(temp.Root, [File("a.txt"), File("A.TXT")])).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        (await ExportFixture.WriteAsync(temp.Root, [File("cards"), File("cards/1.html")])).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        (await ExportFixture.WriteAsync(temp.Root, [File("a.txt"), File("a.txt")])).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        Directory.Exists(temp.Root).ShouldBeFalse();
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("ORB-1")]
    [InlineData("x/y")]
    [InlineData("")]
    [InlineData(".staging-x")]
    [InlineData("-leading")]
    [InlineData("x.previous")]
    public async Task A_run_id_outside_the_pattern_is_refused(string runId)
    {
        using var temp = new TempFolder();

        var result = await ExportFixture.WriteAsync(temp.Root, runId: runId);

        result.Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        Directory.Exists(temp.Root).ShouldBeFalse();
    }

    [Fact]
    public async Task A_run_id_of_81_characters_is_refused_and_80_is_written()
    {
        using var temp = new TempFolder();

        (await ExportFixture.WriteAsync(temp.Root, runId: new string('a', 81))).Error.Code.ShouldBe(ExportErrors.PathInvalidCode);
        (await ExportFixture.WriteAsync(temp.Root, runId: new string('a', 80))).IsSuccess.ShouldBeTrue();
    }

    // the built names

    [Fact]
    public async Task Everything_the_writer_makes_lies_under_the_sim_folder_of_the_root()
    {
        using var temp = new TempFolder();
        ExportFile[] files = [File("cards/0001-NSE_M&M.html"), .. ExportFixture.Files()];

        await ExportFixture.WriteAsync(temp.Root, files);

        ExportFixture.Tree(temp.Path).ShouldAllBe(e => e == "exports/" || e.StartsWith("exports/sim", StringComparison.Ordinal));
        Directory.GetFileSystemEntries(temp.Path).Length.ShouldBe(1);
    }

    public static TheoryData<string, string?> BuiltNames => new()
    {
        { "trades.csv", "/r/sim/run/trades.csv" },
        { "open_at_end.csv", "/r/sim/run/open_at_end.csv" },
        { "cards/0001-x.html", "/r/sim/run/cards/0001-x.html" },
        { "cards/0001-NSE_M&M-buy-2026-03-02-target.html", "/r/sim/run/cards/0001-NSE_M&M-buy-2026-03-02-target.html" },
        { "a.b.c", "/r/sim/run/a.b.c" },
        { "x-y_z", "/r/sim/run/x-y_z" },
        { "./a", "/r/sim/run/a" },
        { "a//b", "/r/sim/run/a/b" },
        { "a/./b", "/r/sim/run/a/b" },
        { "..", null },
        { "../a", null },
        { "a/../../b", null },
        { "a/../..", null },
        { "/etc/passwd", null },
        { "a/../../../etc/passwd", null },
    };

    [Theory]
    [MemberData(nameof(BuiltNames))]
    public void A_built_name_is_the_exact_path_under_the_run_folder_or_it_is_refused(string relative, string? expected)
    {
        if (expected is null)
        {
            Should.Throw<InvalidOperationException>(() => ExportPaths.Combine("/r/sim/run", relative));
        }
        else
        {
            ExportPaths.Combine("/r/sim/run", relative).ShouldBe(expected);
        }
    }

    [Fact]
    public void The_file_name_check_accepts_the_names_of_this_slice_and_of_the_cards()
    {
        ExportPaths.FileNamesProblem([File("README.txt"), File("open_at_end.csv"), File("run.json"), File("trades.csv"), File("index.html"), File("cards/0001-x.html"), File("cards/0002-y.html")]).ShouldBeNull();
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_not_inside_the_folder()
    {
        Should.Throw<InvalidOperationException>(() => ExportPaths.Combine("/r/sim/run", "../run-evil/x"));
    }

    [Fact]
    public async Task An_existing_file_where_a_file_is_to_be_created_inside_a_foreign_staging_name_does_not_stop_a_run()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Root, "sim"));
        System.IO.File.WriteAllText(Path.Combine(temp.Root, "sim", ".staging-" + ExportFixture.RunId + "-00000000"), "a file");

        var result = await ExportFixture.WriteAsync(temp.Root);

        result.IsSuccess.ShouldBeTrue();
        result.Value.StaleStaging.ShouldBeEmpty("a file is not a folder; it is left alone");
        System.IO.File.Exists(Path.Combine(temp.Root, "sim", ".staging-" + ExportFixture.RunId + "-00000000")).ShouldBeTrue();
    }
}
