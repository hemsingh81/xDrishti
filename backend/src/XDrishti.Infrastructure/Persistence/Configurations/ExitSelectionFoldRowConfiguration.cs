using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XDrishti.Infrastructure.Research;

namespace XDrishti.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>research.exit_selection_folds</c> (spec P4-06, D10): one row per fold and candidate of a stored exit choice, keyed by the choice and the place in it. Insert-only for the runtime role, like its parent; a foreign key without cascade.
/// </summary>
internal sealed class ExitSelectionFoldRowConfiguration : IEntityTypeConfiguration<ExitSelectionFoldRow>
{
    public void Configure(EntityTypeBuilder<ExitSelectionFoldRow> builder)
    {
        builder.ToTable("exit_selection_folds", Schemas.Research, t =>
        {
            t.HasCheckConstraint("ck_exit_selection_folds_counts", "fold_index >= 1 AND train_trades >= 0 AND test_trades >= 0 AND test_end >= test_start");
        });
        builder.HasKey(x => new { x.SelectionId, x.FoldIndex, x.CandidateId }).HasName("pk_exit_selection_folds");
        builder.Property(x => x.CandidateId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TestStart).IsRequired();
        builder.Property(x => x.TestEnd).IsRequired();
        builder.Property(x => x.Qualified).IsRequired();
        builder.Property(x => x.TrainTrades).IsRequired();
        builder.Property(x => x.TestTrades).IsRequired();
        builder.Property(x => x.IsWinner).IsRequired();
        builder.HasOne<ExitSelectionRow>().WithMany().HasForeignKey(x => x.SelectionId).HasConstraintName("fk_exit_selection_folds_selection").OnDelete(DeleteBehavior.Restrict);
    }
}
