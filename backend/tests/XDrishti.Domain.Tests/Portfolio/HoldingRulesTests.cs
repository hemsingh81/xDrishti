using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Tests.Portfolio;

public sealed class HoldingRulesTests
{
    [Fact]
    public void A_bad_date_adds_one_field_error_with_the_given_name()
    {
        var errors = new FieldErrors();

        HoldingRules.CheckDate(errors, "rows[2].acquiredOn", HoldingData.Today.AddDays(1), HoldingData.Today);
        HoldingRules.CheckDate(errors, "defaultDate", new DateOnly(1980, 1, 1), HoldingData.Today);
        HoldingRules.CheckDate(errors, "fine", new DateOnly(1990, 1, 1), HoldingData.Today);

        errors.Items.Select(i => (i.Field, i.Message)).ShouldBe([("rows[2].acquiredOn", "Cannot be in the future."), ("defaultDate", "Cannot be before 1990-01-01.")]);
    }
}
