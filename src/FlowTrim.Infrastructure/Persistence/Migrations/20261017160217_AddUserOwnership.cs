using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                table: "subscriptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                // Las filas previas a la autenticación quedan bajo un propietario técnico inaccesible;
                // la base de demo local se reinicia (ver spec HU-12 §7).
                defaultValue: "legacy-owner");

            // El valor por defecto solo sirve para rellenar las filas existentes: se elimina para que
            // ninguna fila nueva pueda recibir un propietario de forma silenciosa.
            migrationBuilder.Sql("ALTER TABLE subscriptions ALTER COLUMN owner_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_owner_id_next_billing_date",
                table: "subscriptions",
                columns: new[] { "owner_id", "next_billing_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_owner_id_not_empty",
                table: "subscriptions",
                sql: "owner_id <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_owner_id_next_billing_date",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_owner_id_not_empty",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "subscriptions");
        }
    }
}
