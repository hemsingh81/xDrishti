using System.Text.RegularExpressions;
using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-02 AC 2 (the static half; the database half is in the integration tests): what the manifest says about the views.</summary>
public sealed class DataManifestTests
{
    [Fact]
    public void There_are_the_documented_views_each_once()
    {
        DataManifest.Views.Select(v => v.Name).ShouldBeUnique();
        DataManifest.Views.Count.ShouldBe(37);
        DataManifest.Views.Where(v => v.Class == ViewClass.C2).Select(v => v.Name).Order().ShouldBe(["fill_grades", "holding_groups", "holding_lots", "holdings", "journal_entries", "journal_fills", "plan_item_allocations", "transactions"]);
    }

    [Fact]
    public void Every_view_and_column_name_is_a_plain_identifier_and_columns_are_unique()
    {
        foreach (var view in DataManifest.Views)
        {
            Regex.IsMatch(view.Name, "^[a-z0-9_]{1,63}$").ShouldBeTrue(view.Name);
            view.Columns.Select(c => c.Name).ShouldBeUnique(view.Name);
            view.Description.ShouldNotBeEmpty();
            view.Grain.ShouldNotBeEmpty();
            view.RowSource.ShouldNotBeEmpty();
            foreach (var column in view.Columns)
            {
                Regex.IsMatch(column.Name, "^[a-z0-9_]{1,63}$").ShouldBeTrue($"{view.Name}.{column.Name}");
                column.Description.ShouldNotBeEmpty($"{view.Name}.{column.Name}");
            }
        }
    }

    [Fact]
    public void No_column_has_a_forbidden_name()
    {
        var offenders = DataManifest.Views.SelectMany(v => v.Columns.Select(c => (View: v.Name, c.Name))).Where(x => DataManifest.IsForbiddenColumnName(x.Name)).Select(x => $"{x.View}.{x.Name}").ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_forbidden_pattern_catches_the_names_it_is_for()
    {
        foreach (var name in new[] { "client_id", "clientid", "api_secret", "access_token", "password", "passwd", "account_no", "account_number", "pan_number", "phone", "owner_email", "dp_id", "dpid", "demat_no", "home_address" })
        {
            DataManifest.IsForbiddenColumnName(name).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "symbol", "qty", "account", "run_key", "r_net", "isin" })
        {
            DataManifest.IsForbiddenColumnName(name).ShouldBeFalse(name);
        }
    }

    [Fact]
    public void Text_columns_are_class_u_and_nothing_else_is()
    {
        foreach (var column in DataManifest.Views.SelectMany(v => v.Columns))
        {
            (column.Kind == ColumnKind.Text).ShouldBe(column.Class == DataClass.U, column.Name);
        }
    }

    [Fact]
    public void A_c2_view_has_c2_or_u_columns_and_a_c1_view_has_c1_or_u_columns()
    {
        foreach (var view in DataManifest.Views)
        {
            var own = view.Class == ViewClass.C2 ? DataClass.C2 : DataClass.C1;
            view.Columns.ShouldAllBe(c => c.Class == own || c.Class == DataClass.U, view.Name);
        }
    }

    [Fact]
    public void Keys_hashes_and_ids_are_never_text()
    {
        var shaped = DataManifest.Views.SelectMany(v => v.Columns).Where(c => c.Name.EndsWith("_key", StringComparison.Ordinal) || c.Name.Contains("hash", StringComparison.Ordinal) || c.Name.Contains("sha256", StringComparison.Ordinal)).ToList();

        shaped.ShouldNotBeEmpty();
        shaped.ShouldAllBe(c => c.Kind != ColumnKind.Text);
    }

    [Theory]
    [InlineData("run_key", ColumnKind.Key)]
    [InlineData("symbol", ColumnKind.Id)]
    [InlineData("cost_total", ColumnKind.Amount)]
    [InlineData("notes", ColumnKind.Text)]
    [InlineData("name", ColumnKind.Text)]
    [InlineData("text", ColumnKind.Text)]
    [InlineData("anything_else", ColumnKind.Text)]
    public void The_kind_of_an_output_column_follows_its_name_and_an_unknown_name_is_text(string name, ColumnKind kind) =>
        DataManifest.KindOfOutput(name).ShouldBe(kind);

    [Fact]
    public void A_name_that_is_text_in_one_view_is_text_in_every_query()
    {
        // 'reason' is code-like in one view and free text in another: the cautious reading wins.
        DataManifest.KindOfOutput("reason").ShouldBe(ColumnKind.Text);
    }
}
