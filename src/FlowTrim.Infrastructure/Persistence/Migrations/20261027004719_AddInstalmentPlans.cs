using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstalmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions");

            migrationBuilder.AddColumn<int>(
                name: "paid_instalments",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "total_instalments",
                table: "subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_instalments",
                table: "subscriptions",
                sql: "((kind = 'Instalment') = (total_instalments IS NOT NULL)) AND paid_instalments >= 0 AND (total_instalments IS NULL OR (total_instalments BETWEEN 1 AND 600 AND paid_instalments <= total_instalments))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions",
                sql: "kind IN ('Subscription', 'Bill', 'Instalment')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_instalments",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "paid_instalments",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "total_instalments",
                table: "subscriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_kind",
                table: "subscriptions",
                sql: "kind IN ('Subscription', 'Bill')");
        }
    }
}
