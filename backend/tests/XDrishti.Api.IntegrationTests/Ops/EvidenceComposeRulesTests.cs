namespace XDrishti.Api.IntegrationTests.Ops;

/// <summary>Spec OPS-01 criterion 3 and ADR 0278: the evidence fetch and admission switches have a compose variable each, and with none set every one renders false or empty (the default after <c>:-</c>).</summary>
public sealed class EvidenceComposeRulesTests
{
    private static readonly string Compose = File.ReadAllText(FindUp("deploy", "compose.yaml"));

    private static string FindUp(params string[] parts)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine([d.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    [Theory]
    [InlineData("Data__History__Nse__Enabled", "XD_HISTORY_NSE_ENABLED", "false")]
    [InlineData("Data__History__Nse__UserAgent", "XD_HISTORY_NSE_USER_AGENT", "")]
    [InlineData("Data__History__Backfill__Enabled", "XD_HISTORY_BACKFILL_ENABLED", "false")]
    [InlineData("Data__History__Replay__AdmitVerified", "XD_REPLAY_ADMIT_VERIFIED", "false")]
    [InlineData("Baskets__System__Fetch__Enabled", "XD_BASKETS_FETCH_ENABLED", "false")]
    [InlineData("Baskets__System__Fetch__UserAgent", "XD_BASKETS_FETCH_USER_AGENT", "")]
    [InlineData("Baskets__System__Lists__FetchEnabled", "XD_BASKETS_LISTS_FETCH_ENABLED", "false")]
    [InlineData("Data__History__Lists__Enabled", "XD_HISTORY_LISTS_ENABLED", "false")]
    [InlineData("Selection__ListFilters__Enabled", "XD_SELECTION_LIST_FILTERS_ENABLED", "false")]
    public void Each_evidence_switch_has_a_variable_that_defaults_off_or_empty(string key, string variable, string fallback)
    {
        Compose.ShouldContain($"      {key}: ${{{variable}:-{fallback}}}");
        (Compose.Split($"{key}:").Length - 1).ShouldBe(1);
    }
}
