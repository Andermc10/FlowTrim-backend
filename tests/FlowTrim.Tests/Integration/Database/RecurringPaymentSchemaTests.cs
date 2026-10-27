using Npgsql;

namespace FlowTrim.Tests.Integration.Database;

/// <summary>
/// Migración <c>AddRecurringPaymentFields</c> (EP-14 HU-72): columnas nuevas, ciclos y tipos admitidos, FK compuestas por
/// propietario y <c>ON DELETE SET NULL</c> limitado a la columna por defecto (D7). SQL directo para saltarse el dominio.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RecurringPaymentSchemaTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private readonly PostgresFixture _database;

    public RecurringPaymentSchemaTests(PostgresFixture database) => _database = database;

    public Task InitializeAsync() => _database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_AddsColumnsConstraintsAndIndexes()
    {
        var columns = await QueryAsync(
            """
            SELECT column_name || ':' || data_type || ':' || is_nullable || COALESCE(':' || character_maximum_length, '') ||
                   COALESCE(':' || column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'subscriptions'
            """);
        Assert.Contains("kind:text:NO:'Subscription'::text", columns);
        Assert.Contains("payment_url:character varying:YES:500", columns);
        Assert.Contains("default_account_id:uuid:YES", columns);
        Assert.Contains("category_id:uuid:YES", columns);

        var foreignKeys = await QueryAsync(
            "SELECT conname || ':' || confdeltype::text FROM pg_constraint " +
            "WHERE contype = 'f' AND conrelid = 'subscriptions'::regclass");
        Assert.Equal(
            ["fk_subscriptions_accounts_owner_id_default_account_id:n", "fk_subscriptions_categories_owner_id_category_id:n"],
            foreignKeys.Order());

        var indexes = await QueryAsync("SELECT indexname FROM pg_indexes WHERE tablename = 'subscriptions'");
        Assert.Contains("ix_subscriptions_owner_id_default_account_id", indexes);
        Assert.Contains("ix_subscriptions_owner_id_category_id", indexes);
    }

    [Theory]
    [InlineData("Weekly")]
    [InlineData("Biweekly")]
    [InlineData("Quarterly")]
    public async Task Insert_NewCycles_AreAccepted(string cycle)
    {
        await InsertSubscriptionAsync(OwnerA, cycle: cycle);

        Assert.Equal(["1"], await QueryAsync("SELECT count(*)::text FROM subscriptions"));
    }

    [Theory]
    [InlineData("'Instalment'", "NULL", "0")]
    [InlineData("'Bill'", "3", "0")]
    [InlineData("'Instalment'", "3", "4")]
    [InlineData("'Instalment'", "601", "0")]
    public async Task Insert_InvalidInstalmentCounts_AreRejectedByCheckConstraint(string kind, string total, string paid)
    {
        // EP-14 HU-74: plan si y solo si hay total; pagadas entre 0 y el total; total entre 1 y 600.
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO subscriptions (id, owner_id, name, amount, currency, billing_cycle, next_billing_date, status, kind, " +
            "total_instalments, paid_instalments, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{OwnerA}', 'Laptop', 100, 'COP', 'Monthly', DATE '2026-10-20', 'Active', {kind}, " +
            $"{total}, {paid}, now(), now())"));

        Assert.Equal("ck_subscriptions_instalments", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_UnknownKind_IsRejectedByCheckConstraint()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertSubscriptionAsync(OwnerA, kind: "Loan"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_subscriptions_kind", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_AnotherOwnersDefaultAccount_IsRejectedByCompositeForeignKey()
    {
        var foreignAccount = await InsertAccountAsync(OwnerB);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertSubscriptionAsync(OwnerA, account: foreignAccount));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_subscriptions_accounts_owner_id_default_account_id", exception.ConstraintName);
    }

    [Fact]
    public async Task DeleteDefaultAccountAndCategory_ClearsOnlyTheDefaults()
    {
        var account = await InsertAccountAsync(OwnerA);
        var category = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO categories (id, owner_id, name, kind, color, icon, is_archived, created_at, updated_at) " +
            $"VALUES ('{category}', '{OwnerA}', 'Home', 'Expense', 'orange', 'home', false, now(), now())");
        await InsertSubscriptionAsync(OwnerA, account: account, category: category);

        await ExecuteAsync($"DELETE FROM accounts WHERE id = '{account}'");
        await ExecuteAsync($"DELETE FROM categories WHERE id = '{category}'");

        Assert.Equal(
            [$"{OwnerA}:<null>:<null>"],
            await QueryAsync(
                "SELECT owner_id || ':' || COALESCE(default_account_id::text, '<null>') || ':' || " +
                "COALESCE(category_id::text, '<null>') FROM subscriptions"));
    }

    private async Task<Guid> InsertAccountAsync(string owner)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(
            "INSERT INTO accounts (id, owner_id, name, type, currency, opening_balance, is_archived, created_at, updated_at) " +
            $"VALUES ('{id}', '{owner}', 'Bank', 'Bank', 'COP', 0, false, now(), now())");
        return id;
    }

    private Task InsertSubscriptionAsync(
        string owner, string cycle = "Monthly", string kind = "Bill", Guid? account = null, Guid? category = null)
    {
        string Nullable(Guid? value) => value is null ? "NULL" : $"'{value}'";
        return ExecuteAsync(
            "INSERT INTO subscriptions (id, owner_id, name, amount, currency, billing_cycle, next_billing_date, status, kind, " +
            "default_account_id, category_id, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{owner}', 'Rent', 100, 'COP', '{cycle}', DATE '2026-10-20', 'Active', '{kind}', " +
            $"{Nullable(account)}, {Nullable(category)}, now(), now())");
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
