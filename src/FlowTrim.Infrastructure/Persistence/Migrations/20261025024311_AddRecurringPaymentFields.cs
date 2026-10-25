using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_billing_cycle",
                table: "subscriptions");

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "default_account_id",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "subscriptions",
                type: "text",
                nullable: false,
                defaultValue: "Subscription");

            migrationBuilder.AddColumn<string>(
                name: "payment_url",
                table: "subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_owner_id_category_id",
                table: "subscriptions",
                columns: new[] { "owner_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_owner_id_default_account_id",
                table: "subscriptions",
                columns: new[] { "owner_id", "default_account_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_billing_cycle",
                table: "subscriptions",
                sql: "billing_cycle IN ('Monthly', 'Yearly', 'Weekly', 'Biweekly', 'Quarterly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions",
                sql: "kind IN ('Subscription', 'Bill')");

            // FK compuestas con owner_id (un pago solo puede apuntar a la cuenta o categoría de su propietario) y
            // ON DELETE SET NULL limitado a la columna por defecto (PostgreSQL >= 15): borrar la cuenta o la categoría
            // solo quita el valor por defecto, nunca owner_id ni el pago (EP-14 D7). EF no modela esta forma.
            migrationBuilder.Sql(
                "ALTER TABLE subscriptions ADD CONSTRAINT fk_subscriptions_accounts_owner_id_default_account_id " +
                "FOREIGN KEY (owner_id, default_account_id) REFERENCES accounts (owner_id, id) " +
                "ON DELETE SET NULL (default_account_id);");
            migrationBuilder.Sql(
                "ALTER TABLE subscriptions ADD CONSTRAINT fk_subscriptions_categories_owner_id_category_id " +
                "FOREIGN KEY (owner_id, category_id) REFERENCES categories (owner_id, id) " +
                "ON DELETE SET NULL (category_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE subscriptions DROP CONSTRAINT fk_subscriptions_categories_owner_id_category_id;");
            migrationBuilder.Sql(
                "ALTER TABLE subscriptions DROP CONSTRAINT fk_subscriptions_accounts_owner_id_default_account_id;");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_owner_id_category_id",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_owner_id_default_account_id",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_billing_cycle",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "default_account_id",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "payment_url",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_billing_cycle",
                table: "subscriptions",
                sql: "billing_cycle IN ('Monthly', 'Yearly')");
        }
    }
}
