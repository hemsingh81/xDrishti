namespace XDrishti.SimExport.Tests.Support;

/// <summary>The golden files of this project: <c>Golden/&lt;name&gt;.golden.txt</c> with the header <c>export xd-sim-export-1</c> (raised with the format when a file changes on purpose); <c>XD_UPDATE_GOLDEN=1</c> rewrites them.</summary>
internal static class Golden
{
    public const string Header = "export xd-sim-export-1";

    /// <summary>The header of the golden files of the cards and the index (P3-04c): they belong to the format <c>xd-sim-export-2</c>.</summary>
    public const string CardHeader = "export xd-sim-export-2";

    public static void Check(string name, string rendered) =>
        XDrishti.Tests.Golden.GoldenFile.Check(name, rendered, Header, "XDrishti.SimExport.Tests.csproj");

    public static void CheckCard(string name, string rendered) =>
        XDrishti.Tests.Golden.GoldenFile.Check(name, rendered, CardHeader, "XDrishti.SimExport.Tests.csproj");
}
