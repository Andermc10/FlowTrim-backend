using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                    table.CheckConstraint("ck_budgets_limit_positive", "limit_amount > 0");
                    table.CheckConstraint("ck_budgets_owner_id_not_empty", "owner_id <> ''");
                    table.ForeignKey(
                        name: "fk_budgets_categories_owner_id_category_id",
                        columns: x => new { x.owner_id, x.category_id },
                        principalTable: "categories",
                        principalColumns: new[] { "owner_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_budgets_owner_id_category_id_currency",
                table: "budgets",
                columns: new[] { "owner_id", "category_id", "currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budgets");
        }
    }
}
