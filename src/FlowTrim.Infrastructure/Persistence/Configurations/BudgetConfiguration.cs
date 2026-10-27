using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Budget"/> a la tabla <c>budgets</c> (EP-15 HU-76). La FK compuesta con <c>owner_id</c> solo
/// admite categorías del mismo propietario; es <c>CASCADE</c> porque un presupuesto es configuración, no historial (D5).
/// </summary>
internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", table =>
        {
            table.HasCheckConstraint("ck_budgets_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_budgets_limit_positive", "limit_amount > 0");
        });

        builder.HasKey(b => b.Id).HasName("pk_budgets");

        builder.Property(b => b.Id).HasColumnType("uuid");
        builder.Property(b => b.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(b => b.CategoryId).HasColumnType("uuid").IsRequired();
        builder.Property(b => b.Currency).HasColumnType("char(3)").IsRequired();
        // "limit" es palabra reservada de SQL: la columna se llama limit_amount.
        builder.Property(b => b.Limit).HasColumnName("limit_amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(b => new { b.OwnerId, b.CategoryId })
            .HasPrincipalKey(c => new { c.OwnerId, c.Id })
            .HasConstraintName("fk_budgets_categories_owner_id_category_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Un presupuesto por categoría y moneda; el índice también cubre la FK.
        builder.HasIndex(b => new { b.OwnerId, b.CategoryId, b.Currency })
            .IsUnique()
            .HasDatabaseName("ux_budgets_owner_id_category_id_currency");
    }
}
