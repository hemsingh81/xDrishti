using XDrishti.Domain.Filings.Extraction;

namespace XDrishti.Domain.Tests.Filings.Extraction;

/// <summary>P15-08 criterion 3: the fixed hostile set is at least 12 invented announcements of a fake company that cover the attacks the spec names.</summary>
public sealed class HostileAnnouncementsTests
{
    [Fact]
    public void The_set_has_at_least_twelve_cases_with_unique_ids_and_a_fake_company()
    {
        HostileAnnouncements.All.Count.ShouldBeGreaterThanOrEqualTo(12);
        HostileAnnouncements.All.Select(c => c.Id).Distinct().Count().ShouldBe(HostileAnnouncements.All.Count);
        HostileAnnouncements.All.Where(c => c.Text.Contains("Zorbex", StringComparison.Ordinal)).Count().ShouldBeGreaterThanOrEqualTo(10);
    }

    private static bool HasRange(string text, char from, char to) => text.Any(c => c >= from && c <= to);

    [Fact]
    public void The_set_covers_a_tool_request_a_fake_system_message_a_fake_fence_an_embedded_answer_a_url_a_repetition_mixed_scripts_zero_width_characters_and_an_over_long_text()
    {
        var texts = HostileAnnouncements.All.Select(c => c.Text).ToList();

        texts.ShouldContain(t => t.Contains("CALL A TOOL", StringComparison.Ordinal));
        texts.ShouldContain(t => t.Contains("[SYSTEM]", StringComparison.Ordinal));
        texts.ShouldContain(t => t.Contains("<<<END", StringComparison.Ordinal));
        texts.ShouldContain(t => t.Contains("\"material\": true", StringComparison.Ordinal));
        texts.ShouldContain(t => t.Contains("http://", StringComparison.Ordinal));
        texts.ShouldContain(t => t.Length == AnnouncementSchema.MaxTextLength && t.Distinct().Count() < 40);
        texts.ShouldContain(t => HasRange(t, '\u0900', '\u097F') && HasRange(t, '\u0400', '\u04FF'));
        texts.ShouldContain(t => t.Contains('​', StringComparison.Ordinal) && t.Contains('⁠', StringComparison.Ordinal));
        texts.ShouldContain(t => t.Length > AnnouncementSchema.MaxTextLength);
        HostileAnnouncements.All.ShouldContain(c => c.Subject.Contains("Ignore previous instructions", StringComparison.Ordinal));
        HostileAnnouncements.All.ShouldContain(c => c.Subject.Contains('‮', StringComparison.Ordinal));
    }
}
