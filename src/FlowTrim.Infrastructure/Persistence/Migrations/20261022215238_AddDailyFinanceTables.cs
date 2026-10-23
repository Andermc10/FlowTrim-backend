using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowTrim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyFinanceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.UniqueConstraint("ux_accounts_owner_id_id", x => new { x.owner_id, x.id });
                    table.CheckConstraint("ck_accounts_owner_id_not_empty", "owner_id <> ''");
                    table.CheckConstraint("ck_accounts_type", "type IN ('Cash', 'Bank', 'CreditCard', 'Savings')");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.UniqueConstraint("ux_categories_owner_id_id", x => new { x.owner_id, x.id });
                    table.CheckConstraint("ck_categories_kind", "kind IN ('Expense', 'Income')");
                    table.CheckConstraint("ck_categories_owner_id_not_empty", "owner_id <> ''");
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transactions_distinct_accounts", "to_account_id <> account_id");
                    table.CheckConstraint("ck_transactions_owner_id_not_empty", "owner_id <> ''");
                    table.CheckConstraint("ck_transactions_shape", "(type = 'Transfer' AND to_account_id IS NOT NULL AND category_id IS NULL) OR (type <> 'Transfer' AND to_account_id IS NULL AND category_id IS NOT NULL)");
                    table.CheckConstraint("ck_transactions_type", "type IN ('Income', 'Expense', 'Transfer')");
                    table.ForeignKey(
                        name: "fk_transactions_accounts_owner_id_account_id",
                        columns: x => new { x.owner_id, x.account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "owner_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_accounts_owner_id_to_account_id",
                        columns: x => new { x.owner_id, x.to_account_id },
                        principalTable: "accounts",
                        principalColumns: new[] { "owner_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_categories_owner_id_category_id",
                        columns: x => new { x.owner_id, x.category_id },
                        principalTable: "categories",
                        principalColumns: new[] { "owner_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_account_id",
                table: "transactions",
                columns: new[] { "owner_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_category_id",
                table: "transactions",
                columns: new[] { "owner_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_date",
                table: "transactions",
                columns: new[] { "owner_id", "date" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_owner_id_to_account_id",
                table: "transactions",
                columns: new[] { "owner_id", "to_account_id" });

            // Nombres únicos por propietario sin distinguir mayúsculas (archivadas incluidas). Son índices de
            // expresión que EF no modela; DropTable los elimina en Down.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_accounts_owner_id_name ON accounts (owner_id, lower(name));");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_categories_owner_id_kind_name ON categories (owner_id, kind, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "categories");
        }
    }
}
