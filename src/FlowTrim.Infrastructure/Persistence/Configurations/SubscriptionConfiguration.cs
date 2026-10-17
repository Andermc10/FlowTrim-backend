using FlowTrim.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Subscription"/> a la tabla <c>subscriptions</c>. Los nombres de columna
/// se generan en snake_case por la convención global (UseSnakeCaseNamingConvention). Aquí se fijan
/// tipos, longitudes, enums como texto, constraints e índices con nombres explícitos.
/// </summary>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions", table =>
        {
            table.HasCheckConstraint("ck_subscriptions_owner_id_not_empty", "owner_id <> ''");
            // Las invariantes también se expresan en la base de datos.
            table.HasCheckConstraint("ck_subscriptions_amount_positive", "amount > 0");
            table.HasCheckConstraint("ck_subscriptions_billing_cycle", "billing_cycle IN ('Monthly', 'Yearly')");
            table.HasCheckConstraint("ck_subscriptions_status", "status IN ('Active', 'Inactive')");
        });

        builder.HasKey(s => s.Id).HasName("pk_subscriptions");

        builder.Property(s => s.Id).HasColumnType("uuid");
        builder.Property(s => s.OwnerId).HasMaxLength(Subscription.OwnerIdMaxLength).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(Subscription.NameMaxLength).IsRequired();
        builder.Property(s => s.Category).HasMaxLength(Subscription.CategoryMaxLength);
        builder.Property(s => s.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.Currency)
            .HasColumnType("char(3)")
            .HasDefaultValue(Subscription.DefaultCurrency)
            .IsRequired();

        // Los enums se guardan como texto legible (con CHECK), no como enteros ni enums nativos de PG.
        builder.Property(s => s.BillingCycle).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasColumnType("text").IsRequired();

        builder.Property(s => s.NextBillingDate).HasColumnType("date").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(s => s.NextBillingDate).HasDatabaseName("ix_subscriptions_next_billing_date");
        builder.HasIndex(s => new { s.OwnerId, s.NextBillingDate })
            .HasDatabaseName("ix_subscriptions_owner_id_next_billing_date");
    }
}
