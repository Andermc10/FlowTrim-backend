using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>Mapeo Fluent de <see cref="UsageRecord"/> a la tabla <c>usage_records</c> con FK a subscriptions.</summary>
internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records", table =>
        {
            table.HasCheckConstraint("ck_usage_records_usage_count_non_negative", "usage_count >= 0");
        });

        builder.HasKey(u => u.Id).HasName("pk_usage_records");

        builder.Property(u => u.Id).HasColumnType("uuid");
        builder.Property(u => u.SubscriptionId).HasColumnType("uuid").IsRequired();
        builder.Property(u => u.UsageDate).HasColumnType("date").IsRequired();
        builder.Property(u => u.UsageCount).HasColumnType("integer").IsRequired();
        builder.Property(u => u.Notes).HasMaxLength(UsageRecord.NotesMaxLength);

        // FK a subscriptions: al borrar una suscripción se borran sus usos (CASCADE).
        builder.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(u => u.SubscriptionId)
            .HasConstraintName("fk_usage_records_subscriptions_subscription_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(u => u.SubscriptionId).HasDatabaseName("ix_usage_records_subscription_id");
    }
}
