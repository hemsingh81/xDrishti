using XDrishti.Domain.Assistant;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-02 AC 8: the lexical pre-check accepts one SELECT over the views and refuses everything else with a reason word. It is for readable errors; the database role is the safety.</summary>
public sealed class SqlPreCheckTests
{
    [Theory]
    [InlineData("select symbol, count(*) from aiv.sim_trades group by 1")]
    [InlineData("SELECT * FROM aiv.instruments;")]
    [InlineData("  with t as (select 1 as n) select n from t  ")]
    [InlineData("select 'a;b' as x, 'it''s -- not a comment' as y")]
    [InlineData("select 'into copy set reset call do' as words")]
    [InlineData("select settings_id, reset_at, do_it, into_x, copy_of from aiv.t")]
    [InlineData("select \"set\" from aiv.t")]
    [InlineData("select $$ drop table x; $$ as body")]
    [InlineData("select $tag$ -- ; /* $tag$ as body")]
    [InlineData("select E'it\\'s;' as e")]
    [InlineData("select symbol from aiv.bars where ts > $1")]
    [InlineData("select count(*) from aiv.sim_trades where (r_net > 0 and (side = 'BUY'))")]
    public void An_acceptable_query_is_accepted(string sql)
    {
        var check = SqlPreCheck.Check(sql);

        check.Accepted.ShouldBeTrue(check.Reason);
        check.Statement.ShouldNotEndWith(";");
    }

    [Theory]
    [InlineData("select 1; select 2", "multiple_statements")]
    [InlineData("select 1; drop table x", "multiple_statements")]
    [InlineData("select 1;; ", "multiple_statements")]
    [InlineData("select 1; -- later", "comment")]
    [InlineData("select 1 /* x */", "comment")]
    [InlineData("select 1 -- x", "comment")]
    [InlineData("sel/**/ect 1", "comment")]
    [InlineData("insert into x values (1)", "not_select")]
    [InlineData("delete from aiv.t", "not_select")]
    [InlineData("explain select 1", "not_select")]
    [InlineData("values (1)", "not_select")]
    [InlineData("(select 1)", "not_select")]
    [InlineData("select * into temp t from aiv.instruments", "into")]
    [InlineData("select 1 where 1 = 1 and set_config('a','b',false) is not null and (select 1) = 1 for update", "update")]
    [InlineData("with x as (insert into t values (1) returning *) select * from x", "insert")]
    [InlineData("select 1 union select 2 copy", "copy")]
    [InlineData("select 1 reset all", "reset")]
    [InlineData("select 1 call x", "call")]
    [InlineData("select 1 do x", "do")]
    [InlineData("select 1 set x", "set")]
    [InlineData("select 1 )", "unbalanced")]
    [InlineData("select (1", "unbalanced")]
    [InlineData("select 'open", "unterminated")]
    [InlineData("select \"open", "unterminated")]
    [InlineData("select $$open", "unterminated")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    public void A_refused_query_gives_a_reason_word(string sql, string reason)
    {
        var check = SqlPreCheck.Check(sql);

        check.Accepted.ShouldBeFalse();
        check.Reason.ShouldBe(reason);
    }

    [Theory]
    [InlineData("ｓｅｌｅｃｔ 1")] // full-width letters
    [InlineData("select 1;")] // Greek question mark, looks like a semicolon
    [InlineData("select 1； select 2")] // full-width semicolon
    [InlineData("select​ 1")] // zero-width space
    [InlineData("select 1\0")]
    [InlineData("sеlect 1")] // Cyrillic e
    public void A_lookalike_character_outside_quoted_text_is_refused(string sql)
    {
        var check = SqlPreCheck.Check(sql);

        check.Accepted.ShouldBeFalse();
        check.Reason.ShouldBe("non_ascii");
    }

    [Fact]
    public void Non_ascii_inside_a_string_literal_is_data_and_is_accepted() =>
        SqlPreCheck.Check("select 'Lakshmī' as name").Accepted.ShouldBeTrue();

    [Fact]
    public void Keywords_are_matched_in_any_case()
    {
        SqlPreCheck.Check("SELECT 1 INTO x").Reason.ShouldBe("into");
        SqlPreCheck.Check("Select 1 Set x").Reason.ShouldBe("set");
    }

    [Fact]
    public void Four_thousand_characters_are_accepted_and_one_more_is_not()
    {
        var accepted = "select '" + new string('a', 4000 - "select ''".Length) + "'";
        accepted.Length.ShouldBe(4000);

        SqlPreCheck.Check(accepted).Accepted.ShouldBeTrue();
        SqlPreCheck.Check(accepted + " ").Reason.ShouldBe("too_long");
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_whitespace_normalised_text()
    {
        var a = SqlPreCheck.Hash("select  1\n from   aiv.t ");
        var b = SqlPreCheck.Hash("select 1 from aiv.t");

        a.ShouldBe(b);
        a.Length.ShouldBe(64);
        SqlPreCheck.Hash("select 2 from aiv.t").ShouldNotBe(a);
    }
}
