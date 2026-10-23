using System.Globalization;
using XDrishti.Domain.Learning;
using XDrishti.Infrastructure.Learning;

namespace XDrishti.Infrastructure.Tests.Learning;

/// <summary>The model ports over the ML.NET adapter (spec P6-07, ADR 0070): train, round trip through the hash-checking loader, FastTree in production, and a loader that refuses a wrong hash.</summary>
public sealed class MlNetModelAdapterTests
{
    private static readonly DateOnly Start = new(2025, 1, 2);

    private static List<ValidationRow> Rows(int count)
    {
        var random = new Random(3);
        var rows = new List<ValidationRow>(count);
        for (var i = 0; i < count; i++)
        {
            var x = random.NextDouble() * 2 - 1;
            var label = random.NextDouble() < 1 / (1 + Math.Exp(-3 * x)) ? 1 : 0;
            var date = Start.AddDays(i);
            var decision = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            rows.Add(new ValidationRow("k" + i.ToString("D4", CultureInfo.InvariantCulture), label, label == 1 ? 1 : -1, date, decision, decision + 3600, "A", [x, random.NextDouble()]));
        }

        return rows;
    }

    [Fact]
    public void The_production_trainer_is_fast_tree_without_a_fall_back_note_and_registers_a_calibration_table_and_importance()
    {
        var trainer = new MlNetModelTrainer();

        var trained = trainer.Train(Rows(300));

        trained.IsSuccess.ShouldBeTrue(trained.IsFailure ? trained.Error.Message : string.Empty);
        trained.Value.Trainer.ShouldBe("FastTree");
        trained.Value.Note.ShouldBeNull();
        trained.Value.Sha256.Length.ShouldBe(64);
        trained.Value.Width.ShouldBe(2);
        trained.Value.CalibrationScores.Count.ShouldBe(trained.Value.CalibrationValues.Count);
        trained.Value.Importance.Count.ShouldBe(2);
        trained.Value.Importance[0].ShouldBeGreaterThan(trained.Value.Importance[1], "feature 0 carries the planted signal");
        trainer.Parameters.ShouldContain("preferred=FastTree");
    }

    [Fact]
    public void The_same_rows_give_the_same_hash_and_the_loader_scores_like_the_trained_model()
    {
        var trainer = new MlNetModelTrainer();
        var rows = Rows(300);
        var one = trainer.Train(rows).Value;
        var two = trainer.Train([.. rows.AsEnumerable().Reverse()]).Value;

        var loaded = new MlNetScorerLoader().Load(one.Bytes, one.Sha256);

        two.Sha256.ShouldBe(one.Sha256);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Error.Message : string.Empty);
        var high = rows[0] with { Features = [0.9, 0.5] };
        var low = rows[0] with { Features = [-0.9, 0.5] };
        loaded.Value.Score(high).ShouldBeGreaterThan(loaded.Value.Score(low));
        loaded.Value.Explain(high, 1).Single().Index.ShouldBe(0);
    }

    [Fact]
    public void The_loader_refuses_a_wrong_hash_and_bytes_that_are_not_a_model()
    {
        var trained = new MlNetModelTrainer().Train(Rows(300)).Value;

        var wrong = new MlNetScorerLoader().Load(trained.Bytes, new string('0', 64));
        var foreign = new MlNetScorerLoader().Load([1, 2, 3], Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 })));

        wrong.IsFailure.ShouldBeTrue();
        wrong.Error.Code.ShouldBe("ml.hash_mismatch");
        foreign.Error.Code.ShouldBe("ml.bad_artifact");
    }

    [Fact]
    public void Too_few_rows_are_a_failure_with_a_code()
    {
        new MlNetModelTrainer().Train(Rows(20)).Error.Code.ShouldBe("ml.too_few_rows");
    }
}
