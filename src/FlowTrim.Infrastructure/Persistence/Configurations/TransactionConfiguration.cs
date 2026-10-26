using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowTrim.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeo Fluent de <see cref="Transaction"/> a la tabla <c>transactions</c> (EP-12). Las FK son compuestas con
/// <c>owner_id</c> (D8): la base de datos rechaza por sí misma un movimiento que apunte a la cuenta o la categoría de
/// otro usuario. Todas son <c>RESTRICT</c>: nunca se borra historial en cascada. La FK al pago recurrente (EP-14 HU-73)
/// usa <c>ON DELETE SET NULL (recurring_payment_id)</c>, que EF no modela: la crea la migración
/// <c>LinkTransactionsToRecurringPayments</c> con SQL.
/// </summary>
internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", table =>
        {
            table.HasCheckConstraint("ck_transactions_owner_id_not_empty", "owner_id <> ''");
            table.HasCheckConstraint("ck_transactions_type", "type IN ('Income', 'Expense', 'Transfer')");
            table.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            // Transferencia ⇔ cuenta destino y sin categoría; ingreso o gasto ⇔ categoría y sin cuenta destino.
            table.HasCheckConstraint(
                "ck_transactions_shape",
                "(type = 'Transfer' AND to_account_id IS NOT NULL AND category_id IS NULL) OR " +
                "(type <> 'Transfer' AND to_account_id IS NULL AND category_id IS NOT NULL)");
            table.HasCheckConstraint("ck_transactions_distinct_accounts", "to_account_id <> account_id");
        });

        builder.HasKey(t => t.Id).HasName("pk_transactions");

        builder.Property(t => t.Id).HasColumnType("uuid");
        builder.Property(t => t.OwnerId).HasMaxLength(OwnerIdentity.MaxLength).IsRequired();
        builder.Property(t => t.Type).HasConversion<string>().HasColumnType("text").IsRequired();
        builder.Property(t => t.AccountId).HasColumnType("uuid").IsRequired();
        builder.Property(t => t.ToAccountId).HasColumnType("uuid");
        builder.Property(t => t.CategoryId).HasColumnType("uuid");
        builder.Property(t => t.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(t => t.Date).HasColumnType("date").IsRequired();
        builder.Property(t => t.Description).HasMaxLength(Transaction.DescriptionMaxLength);
        builder.Property(t => t.Note).HasMaxLength(Transaction.NoteMaxLength);
        builder.Property(t => t.RecurringPaymentId).HasColumnType("uuid");
        builder.Property(t => t.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => new { t.OwnerId, t.AccountId })
            .HasPrincipalKey(a => new { a.OwnerId, a.Id })
            .HasConstraintName("fk_transactions_accounts_owner_id_account_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Con to_account_id nulo la FK no se evalúa (MATCH SIMPLE): solo aplica a las transferencias.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => new { t.OwnerId, t.ToAccountId })
            .HasPrincipalKey(a => new { a.OwnerId, a.Id })
            .HasConstraintName("fk_transactions_accounts_owner_id_to_account_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(t => new { t.OwnerId, t.CategoryId })
            .HasPrincipalKey(c => new { c.OwnerId, c.Id })
            .HasConstraintName("fk_transactions_categories_owner_id_category_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Listado del mes (más recientes primero) y sumas por cuenta/categoría; cubren también las FK.
        builder.HasIndex(t => new { t.OwnerId, t.Date })
            .IsDescending(false, true)
            .HasDatabaseName("ix_transactions_owner_id_date");
        builder.HasIndex(t => new { t.OwnerId, t.AccountId }).HasDatabaseName("ix_transactions_owner_id_account_id");
        builder.HasIndex(t => new { t.OwnerId, t.ToAccountId }).HasDatabaseName("ix_transactions_owner_id_to_account_id");
        builder.HasIndex(t => new { t.OwnerId, t.CategoryId }).HasDatabaseName("ix_transactions_owner_id_category_id");
        builder.HasIndex(t => new { t.OwnerId, t.RecurringPaymentId })
            .HasDatabaseName("ix_transactions_owner_id_recurring_payment_id");
    }
}
