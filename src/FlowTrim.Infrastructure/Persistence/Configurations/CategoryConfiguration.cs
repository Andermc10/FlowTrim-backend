using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Category"/> a la tabla <c>categories</c> (EP-12). Color e ícono no tienen CHECK: sus
/// listas viven en el dominio y pueden crecer sin migración. El índice único <c>ux_categories_owner_id_kind_name</c>
/// sobre <c>lower(name)</c> lo crea la migración con SQL.
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", table =>
        {
            table.HasCheckConstraint("ck_categories_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_categories_kind", "kind IN ('Expense', 'Income')");
        });

        builder.HasKey(c => c.Id).HasName("pk_categories");
        builder.HasAlternateKey(c => new { c.OwnerId, c.Id }).HasName("ux_categories_owner_id_id");

        builder.Property(c => c.Id).HasColumnType("uuid");
        builder.Property(c => c.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(Category.NameMaxLength).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(c => c.Color).HasColumnType("text").IsRequired();
        builder.Property(c => c.Icon).HasColumnType("text").IsRequired();
        builder.Property(c => c.IsArchived).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnType("timestamptz").IsRequired();
    }
}
