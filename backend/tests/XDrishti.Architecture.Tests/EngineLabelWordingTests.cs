namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P3-08, Annex C, D13 and criterion C6: the engine label changed from "not yet verified" to what the oracle comparison showed (ADR 0047), and no current-state text may go on saying the old thing. The words are searched in the
/// source of the backend, the frontend, the prototype, the specs and the design documents; the places that keep the old words on purpose (they describe the label as it was, or quote it to describe the change) are an explicit list with a
/// reason each, so that an exemption cannot grow unnoticed. Historical ADR text is outside the search (an ADR is a dated record and is not edited).
/// </summary>
public sealed class EngineLabelWordingTests
{
    private const string OldWords = "not yet verified";

    /// <summary>The files that may still hold the old words, relative to the repository root, each with why.</summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["docs/specs/P3-04b1-the-export-library-files-labels-and-folder-writer.md"] = "the build record of the slice that introduced the label: it states the contract as it was written then",
        ["docs/specs/P3-08-independent-walker-oracle-and-leak-properties.md"] = "the spec of the change quotes the old label to say what changes",
        ["backend/src/XDrishti.Domain/Research/RunLabels.cs"] = "the engine line of a run of another engine version says it is not yet verified for that version: the fallback is the point of the version binding",
    };

    private static readonly string[] Roots = ["backend/src", "frontend/src", "docs/prototype", "docs/specs", "docs/design"];

    private static readonly string[] Extensions = [".cs", ".ts", ".tsx", ".js", ".html", ".md", ".json", ".css"];

    private static string RepositoryRoot => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props")))!;

    private static List<string> FilesWithTheOldWords()
    {
        var root = RepositoryRoot;
        var found = new List<string>();
        foreach (var folder in Roots)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (RepositoryFiles.IsBuildOutput(file) || file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || !Extensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains(OldWords, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        return [.. found.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void No_current_state_text_still_says_the_engine_is_not_yet_verified_apart_from_the_listed_places()
    {
        FilesWithTheOldWords().Except(Exempt.Keys).ShouldBeEmpty("update the wording to the engine line of RunLabels (ADR 0047), or add the file to the exempt list with its reason");
    }

    [Fact]
    public void Every_exempt_place_really_holds_the_old_words_so_the_list_cannot_outlive_its_reason()
    {
        FilesWithTheOldWords().ShouldBe([.. Exempt.Keys.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public void The_search_does_find_the_old_words_in_the_places_that_keep_them_a_sentinel_for_the_rule_itself()
    {
        FilesWithTheOldWords().ShouldNotBeEmpty("a search that finds nothing anywhere would pass for the wrong reason");
    }

    [Fact]
    public void The_engine_line_of_the_label_is_the_wording_the_owner_approved()
    {
        XDrishti.Domain.Research.RunLabels.EngineVerification.ShouldBe("engine: agrees with a separately written reference implementation of the rule tables (P3-08, sim-5); not checked against charts or the owner's charge sheet");
    }
}
