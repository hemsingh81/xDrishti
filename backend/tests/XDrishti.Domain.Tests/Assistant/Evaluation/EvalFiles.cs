global using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Finds the files of the repo's <c>eval/</c> folder from the test output folder (the files are the data of the eval, read as they are checked in).</summary>
internal static class EvalFiles
{
    public static string Path(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new FileNotFoundException("eval/ folder not found above " + AppContext.BaseDirectory) : System.IO.Path.Combine(dir.FullName, "eval", relative);
    }

    public static string Text(string relative) => File.ReadAllText(Path(relative));

    public static IReadOnlyList<EvalCase> DataQuestions()
    {
        var read = EvalCaseReader.ReadLines(Text("sets/data-questions.jsonl"));
        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Message : string.Empty);
        return read.Value;
    }

    public static EvalFixture Fixture()
    {
        var read = EvalFixtureReader.Parse(Text("fixture/fixture.json"));
        read.IsSuccess.ShouldBeTrue(read.IsFailure ? read.Error.Message : string.Empty);
        return read.Value;
    }
}
