using System.Text.Json;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P15-07 criterion 8 (decision 4): announcement text is third-party text and stays in the git-ignored <c>data/</c>. Nothing under <c>eval/</c> holds a <c>text</c> field of the sample (the committed manifest is ids and hashes), no labelled-text file sits under <c>eval/</c>,
/// the baseline is committed, and <c>.gitignore</c> covers <c>data/eval/</c>.
/// </summary>
public sealed class AnnouncementEvalHygieneTests
{
    private static string Eval => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("eval", "thresholds.json"))!;

    [Fact]
    public void The_gitignore_covers_data_eval_so_the_sample_and_the_labelled_text_never_reach_git()
    {
        var ignore = File.ReadAllLines(RepositoryFiles.RepositoryFile(".gitignore")).Select(l => l.Trim()).ToList();

        ignore.ShouldContain("data/**");
        ignore.ShouldNotContain("!data/eval/");
        ignore.ShouldNotContain("!data/eval/**");
    }

    [Fact]
    public void Nothing_under_eval_holds_a_text_field_of_the_sample_or_a_labelled_text_file()
    {
        foreach (var file in Directory.EnumerateFiles(Eval, "*", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var name = Path.GetFileName(file);
            name.ShouldNotBe("to-label.csv");
            name.ShouldNotBe("announcements.jsonl");
            name.ShouldNotBe("sample.json");
        }

        var manifest = Path.Combine(Eval, "sets", "announcements.manifest.json");
        if (File.Exists(manifest))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            Collect(json.RootElement, keys);
            keys.ShouldNotContain("text");
            keys.ShouldNotContain("subject");
            keys.ShouldNotContain("headline");
            keys.ShouldNotContain("category");
        }
    }

    [Fact]
    public void The_pre_registered_baseline_is_committed_under_eval_sets()
    {
        File.Exists(Path.Combine(Eval, "sets", "announcement-baseline.json")).ShouldBeTrue();
    }

    private static void Collect(JsonElement element, HashSet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    keys.Add(property.Name);
                    Collect(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, keys);
                }

                break;
        }
    }
}
