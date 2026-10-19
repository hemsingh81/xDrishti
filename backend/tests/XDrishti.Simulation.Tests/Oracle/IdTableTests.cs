using System.Reflection;

namespace XDrishti.Simulation.Tests.Oracle;

public sealed class IdTableTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_encoded_id_points_at_an_existing_row_or_test_and_every_other_id_has_a_reason()
    {
        var rowIds = AllRows.Rows.Select(r => r.Id).ToHashSet();
        var lines = new List<string>();
        int encoded = 0;
        foreach (IdTable.Entry e in IdTable.All)
        {
            if (e.Reason is not null)
            {
                e.Where.ShouldBeEmpty();
                e.Reason.Length.ShouldBeGreaterThan(20);
                lines.Add($"{e.Spec} {e.Id}: NOT ENCODED - {e.Reason}");
                continue;
            }

            e.Where.ShouldNotBeEmpty(e.Id);
            foreach (string w in e.Where)
            {
                if (w.StartsWith("row:", StringComparison.Ordinal))
                {
                    rowIds.ShouldContain(w[4..], $"{e.Spec} {e.Id}");
                }
                else
                {
                    string[] parts = w[5..].Split('.');
                    Type type = typeof(IdTableTests).Assembly.GetTypes().Single(t => t.Name == parts[0]);
                    type.GetMethod(parts[1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).ShouldNotBeNull(w);
                }
            }

            encoded++;
            lines.Add($"{e.Spec} {e.Id}: encoded in {string.Join(", ", e.Where)}");
        }

        lines.Insert(0, $"ids in the table: {IdTable.All.Count}; encoded: {encoded}; named with a reason: {IdTable.All.Count - encoded}; hand rows (HandRow data): {AllRows.Rows.Count}");
        Report.Write("id-table.txt", lines);
        output.WriteLine(lines[0]);
        encoded.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void Every_encoded_hand_row_is_named_by_the_id_table_or_is_a_supplementary_row()
    {
        var named = IdTable.All.SelectMany(e => e.Where).Where(w => w.StartsWith("row:", StringComparison.Ordinal)).Select(w => w[4..]).ToHashSet();
        foreach (HandRow r in AllRows.Rows)
        {
            if (r.Source == "O-own" || r.Id.StartsWith("S-", StringComparison.Ordinal))
            {
                continue;
            }

            named.ShouldContain(r.Id);
        }
    }
}
