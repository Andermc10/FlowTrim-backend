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
                // Las filas existentes pertenecen a la base de datos local previa a la autenticación.
                // Se conservan bajo un propietario técnico hasta que el entorno defina su migración real.
                defaultValue: "legacy-owner");

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
