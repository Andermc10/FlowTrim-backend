using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Account"/> a la tabla <c>accounts</c> (EP-12). El índice único de nombre sin distinguir
/// mayúsculas (<c>ux_accounts_owner_id_name</c> sobre <c>lower(name)</c>) es una expresión que EF no modela: lo crea
/// la migración <c>AddDailyFinanceTables</c> con SQL.
/// </summary>
internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_accounts_type", "type IN ('Cash', 'Bank', 'CreditCard', 'Savings')");
        });

        builder.HasKey(a => a.Id).HasName("pk_accounts");

        // Destino de las FK compuestas de transactions (EP-12 D8): un movimiento solo puede apuntar a una cuenta
        // del mismo propietario.
        builder.HasAlternateKey(a => new { a.OwnerId, a.Id }).HasName("ux_accounts_owner_id_id");

        builder.Property(a => a.Id).HasColumnType("uuid");
        builder.Property(a => a.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(Account.NameMaxLength).IsRequired();
        builder.Property(a => a.Type).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(a => a.Currency).HasColumnType("char(3)").IsRequired();
        builder.Property(a => a.OpeningBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(a => a.IsArchived).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnType("timestamptz").IsRequired();
    }
}
