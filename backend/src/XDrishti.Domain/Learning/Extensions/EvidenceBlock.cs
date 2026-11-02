namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The common parts of the evidence block of every protocol (P8-00 D3): the named missing and failed quantities, written the same way everywhere. An unavailable value is the string <c>unavailable</c>, never a number.</summary>
internal static class EvidenceBlock
{
    /// <summary>Writes the <c>missing</c> and <c>failed</c> arrays and the verdict word into the open object.</summary>
    public static void Close(ExtensionJson json, ExtensionVerdict verdict, IReadOnlyList<string> missing, IReadOnlyList<string> failed)
    {
        json.Text("verdict", verdict.ToWord()).Array("missing");
        foreach (var m in missing)
        {
            json.Item(m);
        }

        json.EndArray().Array("failed");
        foreach (var f in failed)
        {
            json.Item(f);
        }

        json.EndArray().EndObject();
    }

    /// <summary>The empty result of a protocol that could not start: NOT_EVALUABLE with the missing quantities named, and no estimate.</summary>
    public static ProtocolOutcome NotEvaluable(string protocol, IReadOnlyList<string> missing, IReadOnlyList<string> limits, int rows = 0, int seasons = 0)
    {
        using var estimates = new ExtensionJson();
        estimates.Object().Text("status", "unavailable").EndObject();
        using var evidence = new ExtensionJson();
        evidence.Object().Text("protocol", protocol);
        Close(evidence, ExtensionVerdict.NotEvaluable, missing, []);
        return new ProtocolOutcome(ExtensionVerdict.NotEvaluable, missing, [], limits, estimates.Finish(), evidence.Finish(), seasons, 0, rows);
    }
}
