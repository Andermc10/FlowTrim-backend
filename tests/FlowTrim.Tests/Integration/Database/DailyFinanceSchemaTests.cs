using Npgsql;

namespace FlowTrim.Tests.Integration.Database;

/// <summary>
/// Migración <c>AddDailyFinanceTables</c> (EP-12 §7): tablas, restricciones de forma, FK compuestas por propietario
/// (D8) e índices únicos sin distinguir mayúsculas. SQL directo para saltarse el dominio.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DailyFinanceSchemaTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private readonly PostgresFixture _database;

    public DailyFinanceSchemaTests(PostgresFixture database) => _database = database;

    public Task InitializeAsync() => _database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_CreatesTablesConstraintsAndIndexes()
    {
        var columns = await QueryAsync(
            """
            SELECT table_name || '.' || column_name || ':' || data_type || ':' || is_nullable ||
                   COALESCE(':' || character_maximum_length, '') ||
                   COALESCE(':' || numeric_precision || ',' || numeric_scale, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('accounts', 'categories', 'transactions')
            """);

        string[] expectedColumns =
        [
            "accounts.owner_id:character varying:NO:200", "accounts.name:character varying:NO:60",
            "accounts.type:text:NO", "accounts.currency:character:NO:3", "accounts.opening_balance:numeric:NO:18,2",
            "accounts.is_archived:boolean:NO", "categories.name:character varying:NO:40", "categories.kind:text:NO",
            "categories.color:text:NO", "categories.icon:text:NO", "transactions.type:text:NO",
            "transactions.account_id:uuid:NO", "transactions.to_account_id:uuid:YES", "transactions.category_id:uuid:YES",
            "transactions.amount:numeric:NO:18,2", "transactions.date:date:NO",
            "transactions.description:character varying:YES:140", "transactions.note:character varying:YES:500",
            "transactions.created_at:timestamp with time zone:NO",
        ];
        Assert.All(expectedColumns, column => Assert.Contains(column, columns));

        var constraints = await QueryAsync(
            "SELECT conname FROM pg_constraint " +
            "WHERE conrelid IN ('accounts'::regclass, 'categories'::regclass, 'transactions'::regclass)");
        string[] expectedConstraints =
        [
            "pk_accounts", "ux_accounts_owner_id_id", "ck_accounts_type", "ck_accounts_owner_id_not_empty",
            "pk_categories", "ux_categories_owner_id_id", "ck_categories_kind", "pk_transactions",
            "ck_transactions_type", "ck_transactions_amount_positive", "ck_transactions_shape",
            "ck_transactions_distinct_accounts", "fk_transactions_accounts_owner_id_account_id",
            "fk_transactions_accounts_owner_id_to_account_id", "fk_transactions_categories_owner_id_category_id",
        ];
        Assert.All(expectedConstraints, name => Assert.Contains(name, constraints));

        // Las FK a cuentas y categorías son RESTRICT; la del pago recurrente (EP-14 HU-73) solo quita el vínculo.
        var restrict = await QueryAsync(
            "SELECT conname || ':' || confdeltype::text FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid = 'transactions'::regclass");
        Assert.Equal(4, restrict.Count);
        Assert.All(
            restrict.Where(foreignKey => !foreignKey.StartsWith("fk_transactions_subscriptions_", StringComparison.Ordinal)),
            foreignKey => Assert.EndsWith(":r", foreignKey));
        Assert.Contains("fk_transactions_subscriptions_owner_id_recurring_payment_id:n", restrict);

        var indexes = await QueryAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");
        string[] expectedIndexes =
        [
            "ux_accounts_owner_id_name", "ux_categories_owner_id_kind_name", "ix_transactions_owner_id_date",
            "ix_transactions_owner_id_account_id", "ix_transactions_owner_id_to_account_id",
            "ix_transactions_owner_id_category_id",
        ];
        Assert.All(expectedIndexes, name => Assert.Contains(name, indexes));
    }

    [Fact]
    public async Task Insert_TransactionWithAnotherOwnersAccount_IsRejectedByCompositeForeignKey()
    {
        var foreignAccount = await InsertAccountAsync(OwnerB, "Owner B bank");
        var category = await InsertCategoryAsync(OwnerA, "Food", "Expense");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTransactionAsync(OwnerA, "Expense", foreignAccount, null, category));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_transactions_accounts_owner_id_account_id", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_TransferToAnotherOwnersAccount_IsRejectedByCompositeForeignKey()
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");
        var foreignAccount = await InsertAccountAsync(OwnerB, "Owner B bank");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTransactionAsync(OwnerA, "Transfer", account, foreignAccount, null));

        Assert.Equal("fk_transactions_accounts_owner_id_to_account_id", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_TransactionWithAnotherOwnersCategory_IsRejectedByCompositeForeignKey()
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");
        var foreignCategory = await InsertCategoryAsync(OwnerB, "Food", "Expense");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTransactionAsync(OwnerA, "Expense", account, null, foreignCategory));

        Assert.Equal("fk_transactions_categories_owner_id_category_id", exception.ConstraintName);
    }

    [Theory]
    [InlineData("Transfer", false, true, "ck_transactions_shape")]
    [InlineData("Expense", true, true, "ck_transactions_shape")]
    [InlineData("Expense", false, false, "ck_transactions_shape")]
    public async Task Insert_InvalidShape_IsRejectedByCheckConstraint(
        string type, bool withDestination, bool withCategory, string constraint)
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");
        var destination = await InsertAccountAsync(OwnerA, "Cash");
        var category = await InsertCategoryAsync(OwnerA, "Food", "Expense");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertTransactionAsync(
            OwnerA, type, account, withDestination ? destination : null, withCategory ? category : null));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_TransferToSameAccount_IsRejected()
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTransactionAsync(OwnerA, "Transfer", account, account, null));

        Assert.Equal("ck_transactions_distinct_accounts", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_ZeroAmount_IsRejected()
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");
        var category = await InsertCategoryAsync(OwnerA, "Food", "Expense");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTransactionAsync(OwnerA, "Expense", account, null, category, amount: "0"));

        Assert.Equal("ck_transactions_amount_positive", exception.ConstraintName);
    }

    [Fact]
    public async Task DeleteAccount_WithTransactions_IsRestricted()
    {
        var account = await InsertAccountAsync(OwnerA, "Bank");
        var category = await InsertCategoryAsync(OwnerA, "Food", "Expense");
        await InsertTransactionAsync(OwnerA, "Expense", account, null, category);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"DELETE FROM accounts WHERE id = '{account}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task Insert_DuplicateNamesIgnoringCase_AreRejectedPerOwner()
    {
        await InsertAccountAsync(OwnerA, "Bank");
        await InsertAccountAsync(OwnerB, "Bank");
        await InsertCategoryAsync(OwnerA, "Other", "Expense");
        await InsertCategoryAsync(OwnerA, "Other", "Income");

        var account = await Assert.ThrowsAsync<PostgresException>(() => InsertAccountAsync(OwnerA, "BANK"));
        var category = await Assert.ThrowsAsync<PostgresException>(() => InsertCategoryAsync(OwnerA, "other", "Expense"));

        Assert.Equal("ux_accounts_owner_id_name", account.ConstraintName);
        Assert.Equal("ux_categories_owner_id_kind_name", category.ConstraintName);
    }

    private async Task<Guid> InsertAccountAsync(string owner, string name)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO accounts (id, owner_id, name, type, currency, opening_balance, is_archived, created_at, updated_at) " +
            $"VALUES ('{id}', '{owner}', '{name}', 'Bank', 'COP', 0, false, now(), now())");
        return id;
    }

    private async Task<Guid> InsertCategoryAsync(string owner, string name, string kind)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO categories (id, owner_id, name, kind, color, icon, is_archived, created_at, updated_at) " +
            $"VALUES ('{id}', '{owner}', '{name}', '{kind}', 'orange', 'food', false, now(), now())");
        return id;
    }

    private Task InsertTransactionAsync(
        string owner, string type, Guid account, Guid? destination, Guid? category, string amount = "10")
    {
        string Nullable(Guid? value) => value is null ? "NULL" : $"'{value}'";
        return ExecuteAsync(
            "INSERT INTO transactions (id, owner_id, type, account_id, to_account_id, category_id, amount, date, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{owner}', '{type}', '{account}', {Nullable(destination)}, {Nullable(category)}, " +
            $"{amount}, DATE '2026-10-20', now(), now())");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
