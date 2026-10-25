using FlowTrim.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Subscription"/> a la tabla <c>subscriptions</c>. Los nombres de columna
/// se generan en snake_case por la convención global (UseSnakeCaseNamingConvention). Aquí se fijan
/// tipos, longitudes, enums como texto, constraints e índices con nombres explícitos. Las FK compuestas de la cuenta y
/// la categoría por defecto usan <c>ON DELETE SET NULL (columna)</c>, que EF no modela: las crea la migración
/// <c>AddRecurringPaymentFields</c> con SQL (EP-14 D7).
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
            table.HasCheckConstraint(
                "ck_subscriptions_billing_cycle",
                "billing_cycle IN ('Monthly', 'Yearly', 'Weekly', 'Biweekly', 'Quarterly')");
            table.HasCheckConstraint("ck_subscriptions_kind", "kind IN ('Subscription', 'Bill')");
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
        // Las filas existentes pasan a ser suscripciones (EP-14 HU-72 FR-1).
        builder.Property(s => s.Kind)
            .HasConversion<string>()
            .HasColumnType("text")
            .HasDefaultValue(SubscriptionKind.Subscription)
            .HasSentinel((SubscriptionKind)0)
            .IsRequired();
        builder.Property(s => s.PaymentUrl).HasMaxLength(Subscription.PaymentUrlMaxLength);
        builder.Property(s => s.DefaultAccountId).HasColumnType("uuid");
        builder.Property(s => s.CategoryId).HasColumnType("uuid");

        builder.Property(s => s.NextBillingDate).HasColumnType("date").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(s => s.NextBillingDate).HasDatabaseName("ix_subscriptions_next_billing_date");
        builder.HasIndex(s => new { s.OwnerId, s.NextBillingDate })
            .HasDatabaseName("ix_subscriptions_owner_id_next_billing_date");
        // Índices de las FK por defecto (creadas con SQL en la migración).
        builder.HasIndex(s => new { s.OwnerId, s.DefaultAccountId })
            .HasDatabaseName("ix_subscriptions_owner_id_default_account_id");
        builder.HasIndex(s => new { s.OwnerId, s.CategoryId }).HasDatabaseName("ix_subscriptions_owner_id_category_id");
    }
}
