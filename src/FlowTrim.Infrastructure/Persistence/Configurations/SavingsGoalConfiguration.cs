using FlowTrim.Domain.Common;
using FlowTrim.Domain.Goals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo de <see cref="SavingsGoal"/> a <c>savings_goals</c> (EP-15 HU-77).</summary>
internal sealed class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.ToTable("savings_goals", table =>
        {
            table.HasCheckConstraint("ck_savings_goals_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_savings_goals_target_positive", "target_amount > 0");
        });

        builder.HasKey(g => g.Id).HasName("pk_savings_goals");
        // Destino de la FK compuesta de los aportes: un aporte solo apunta a una meta del mismo propietario.
        builder.HasAlternateKey(g => new { g.OwnerId, g.Id }).HasName("ux_savings_goals_owner_id_id");

        builder.Property(g => g.Id).HasColumnType("uuid");
        builder.Property(g => g.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(SavingsGoal.NameMaxLength).IsRequired();
        builder.Property(g => g.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(g => g.Target).HasColumnName("target_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(g => g.TargetDate).HasColumnType("date");
        builder.Property(g => g.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(g => g.UpdatedAt).HasColumnType("timestamptz").IsRequired();
    }
}

/// <summary>
/// Mapeo de <see cref="GoalContribution"/> a <c>goal_contributions</c>. Borrar la meta borra sus aportes (CASCADE): son
/// declaraciones de esa meta, no movimientos de cuentas (EP-15 D9).
/// </summary>
internal sealed class GoalContributionConfiguration : IEntityTypeConfiguration<GoalContribution>
{
    public void Configure(EntityTypeBuilder<GoalContribution> builder)
    {
        builder.ToTable("goal_contributions", table =>
        {
            table.HasCheckConstraint("ck_goal_contributions_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_goal_contributions_amount_not_zero", "amount <> 0");
        });

        builder.HasKey(c => c.Id).HasName("pk_goal_contributions");

        builder.Property(c => c.Id).HasColumnType("uuid");
        builder.Property(c => c.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(c => c.GoalId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(c => c.Date).HasColumnType("date").IsRequired();
        builder.Property(c => c.Note).HasMaxLength(GoalContribution.NoteMaxLength);
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasOne<SavingsGoal>()
            .WithMany()
            .HasForeignKey(c => new { c.OwnerId, c.GoalId })
            .HasPrincipalKey(g => new { g.OwnerId, g.Id })
            .HasConstraintName("fk_goal_contributions_savings_goals_owner_id_goal_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.OwnerId, c.GoalId, c.Date }).HasDatabaseName("ix_goal_contributions_owner_id_goal_id_date");
    }
}
