using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkTransactionsToRecurringPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recurring_payment_id",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ux_subscriptions_owner_id_id",
                table: "subscriptions",
                columns: new[] { "owner_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_recurring_payment_id",
                table: "transactions",
                columns: new[] { "owner_id", "recurring_payment_id" });

            // FK compuesta con owner_id (un movimiento solo apunta a un pago de su propietario) y ON DELETE SET NULL
            // limitado a la columna: borrar el pago conserva el movimiento y solo quita el vínculo (EP-14 D6, HU-73 AC-3).
            migrationBuilder.Sql(
                "ALTER TABLE transactions ADD CONSTRAINT fk_transactions_subscriptions_owner_id_recurring_payment_id " +
                "FOREIGN KEY (owner_id, recurring_payment_id) REFERENCES subscriptions (owner_id, id) " +
                "ON DELETE SET NULL (recurring_payment_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE transactions DROP CONSTRAINT fk_transactions_subscriptions_owner_id_recurring_payment_id;");

            migrationBuilder.DropIndex(
                name: "ix_transactions_owner_id_recurring_payment_id",
                table: "transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_subscriptions_owner_id_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "recurring_payment_id",
                table: "transactions");
        }
    }
}
