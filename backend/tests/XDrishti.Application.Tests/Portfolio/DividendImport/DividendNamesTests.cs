using XDrishti.Application.Portfolio.DividendImport;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>The scrip-name normaliser (ADR 0028, P2-15 criterion 4): case, punctuation, ampersand and corporate suffixes are ignored; containment is by whole words and needs two significant words.</summary>
public sealed class DividendNamesTests
{
    [Theory]
    [InlineData("Power Finance Corporation", "POWER FINANCE CORPORATION LTD.", true)]
    [InlineData("Mahindra & Mahindra", "MAHINDRA AND MAHINDRA LIMITED", true)]
    [InlineData("Mahindra & Mahindra Ltd", "Mahindra and Mahindra", true)]
    [InlineData("Amara Raja Energy & Mobility", "AMARA RAJA ENERGY AND MOBILITY LTD", true)]
    [InlineData("Dr. Lal Path Labs", "DR LAL PATH LABS LIMITED", true)]
    [InlineData("N.M.D.C.", "NMDC Limited", true)]
    [InlineData("DIC India", "DIC INDIA LTD", true)]
    [InlineData("Engineers India Limited", "Engineers", true)]
    [InlineData("Orion  Steel   Industries", "orion-steel", true)]
    [InlineData("Nestle's Foods", "NESTLES FOODS LTD", true)]
    [InlineData("Tata Motors", "Tata Motors Passenger Vehicles", false)]
    [InlineData("Bank of India", "Union Bank of India", false)]
    [InlineData("Orion Steel", "Orion Steels", false)]
    public void Two_names_are_one_key_when_only_case_punctuation_and_company_suffixes_differ(string first, string second, bool same)
    {
        (DividendNames.Key(first) == DividendNames.Key(second)).ShouldBe(same);
    }

    [Fact]
    public void The_key_is_the_lower_case_words_without_suffixes()
    {
        DividendNames.Key("  POWER Finance, Corporation Ltd. ").ShouldBe("power finance");
        DividendNames.Key("Mahindra & Mahindra").ShouldBe("mahindra and mahindra");
        DividendNames.Key("Steel Authority of India").ShouldBe("steel authority of");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("---")]
    public void A_name_without_letters_or_digits_has_an_empty_key(string name)
    {
        DividendNames.Key(name).ShouldBeEmpty();
    }

    [Fact]
    public void A_name_made_only_of_suffixes_keeps_them()
    {
        DividendNames.Key("India Limited").ShouldBe("india limited");
    }

    [Fact]
    public void A_very_long_key_is_cut_to_200_characters()
    {
        DividendNames.Key(string.Join(' ', Enumerable.Repeat("abcdefgh", 60))).Length.ShouldBeLessThanOrEqualTo(DividendNames.MaxKeyLength);
    }

    [Theory]
    [InlineData("kirloskar oil", "kirloskar oil engines", true)]
    [InlineData("kirloskar oil engines", "kirloskar oil", true)]
    [InlineData("oil engines", "kirloskar oil engines", true)]
    [InlineData("kirloskar engines", "kirloskar oil engines", false)]
    [InlineData("bank of", "union bank of", false)]
    [InlineData("tata", "tata motors", false)]
    [InlineData("tata motors", "tata motors", false)]
    [InlineData("kirloskar oil", "kirloskarx oil engines", false)]
    [InlineData("the of", "the of and", false)]
    public void One_key_is_inside_another_as_whole_consecutive_words_when_the_shorter_has_two_significant_words(string first, string second, bool contained)
    {
        DividendNames.Contains(first, second).ShouldBe(contained);
    }
}
