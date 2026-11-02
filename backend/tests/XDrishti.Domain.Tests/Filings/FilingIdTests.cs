using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

public sealed class FilingIdTests
{
    [Fact]
    public void The_same_inputs_give_the_same_id()
    {
        var a = FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(1));
        var b = FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(1));

        a.ShouldBe(b);
    }

    [Fact]
    public void Each_input_changes_the_id()
    {
        var baseId = FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(1));

        FilingId.Create(Guid.NewGuid(), FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(1)).ShouldNotBe(baseId);
        FilingId.Create(FilingSamples.Instrument, FilingKind.Shareholding, new DateOnly(2026, 6, 30), FilingSamples.Sha(1)).ShouldNotBe(baseId);
        FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 3, 31), FilingSamples.Sha(1)).ShouldNotBe(baseId);
        FilingId.Create(FilingSamples.Instrument, FilingKind.Results, null, FilingSamples.Sha(1)).ShouldNotBe(baseId);
        FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(2)).ShouldNotBe(baseId);
    }

    [Fact]
    public void The_id_is_the_first_sixteen_bytes_of_the_sha256_of_the_pipe_joined_text()
    {
        var text = $"{FilingSamples.Instrument:D}|results|2026-06-30|{FilingSamples.Sha(7)}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));

        FilingId.Create(FilingSamples.Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingSamples.Sha(7)).ToByteArray(bigEndian: true).ShouldBe(hash[..16]);
    }
}
