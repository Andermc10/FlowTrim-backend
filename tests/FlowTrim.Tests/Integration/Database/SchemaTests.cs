using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowTrim.Tests.Integration.Database;

/// <summary>Migraciones, esquema y restricciones de PostgreSQL (HU-47, AC-1..AC-3).</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SchemaTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgresFixture _database;

    public SchemaTests(PostgresFixture database) => _database = database;

    public Task InitializeAsync() => _database.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migrations_EmptyDatabase_CreateExpectedSchema()
    {
        var columns = await QueryAsync(
            """
            SELECT table_name || '.' || column_name || ':' || data_type || ':' || is_nullable ||
                   COALESCE(':' || character_maximum_length, '') ||
                   COALESCE(':' || numeric_precision || ',' || numeric_scale, '')
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('subscriptions', 'usage_records')
            """);

        Assert.Contains("subscriptions.id:uuid:NO", columns);
        Assert.Contains("subscriptions.owner_id:character varying:NO:200", columns);
        Assert.Contains("subscriptions.name:character varying:NO:120", columns);
        Assert.Contains("subscriptions.category:character varying:YES:60", columns);
        Assert.Contains("subscriptions.amount:numeric:NO:18,2", columns);
        Assert.Contains("subscriptions.currency:character:NO:3", columns);
        Assert.Contains("subscriptions.billing_cycle:text:NO", columns);
        Assert.Contains("subscriptions.status:text:NO", columns);
        Assert.Contains("subscriptions.next_billing_date:date:NO", columns);
        Assert.Contains("subscriptions.created_at:timestamp with time zone:NO", columns);
        Assert.Contains("subscriptions.updated_at:timestamp with time zone:NO", columns);
        Assert.Contains("usage_records.subscription_id:uuid:NO", columns);
        Assert.Contains("usage_records.usage_date:date:NO", columns);
        Assert.Contains("usage_records.usage_count:integer:NO:32,0", columns);
        Assert.Contains("usage_records.notes:character varying:YES:500", columns);

        var constraints = await QueryAsync(
            "SELECT conname FROM pg_constraint WHERE conrelid IN ('subscriptions'::regclass, 'usage_records'::regclass)");
        string[] expectedConstraints =
        [
            "pk_subscriptions", "pk_usage_records", "fk_usage_records_subscriptions_subscription_id",
            "ck_subscriptions_amount_positive", "ck_subscriptions_owner_id_not_empty",
            "ck_subscriptions_billing_cycle", "ck_subscriptions_status", "ck_usage_records_usage_count_non_negative",
        ];
        Assert.All(expectedConstraints, name => Assert.Contains(name, constraints));

        var indexes = await QueryAsync("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'");
        Assert.Contains("ix_subscriptions_next_billing_date", indexes);
        Assert.Contains("ix_subscriptions_owner_id_next_billing_date", indexes);
        Assert.Contains("ix_usage_records_subscription_id", indexes);

        // Ninguna columna conserva el DEFAULT temporal de AddUserOwnership.
        var ownerDefault = await QueryAsync(
            "SELECT COALESCE(column_default, '<none>') FROM information_schema.columns " +
            "WHERE table_name = 'subscriptions' AND column_name = 'owner_id'");
        Assert.Equal(["<none>"], ownerDefault);
    }

    [Fact]
    public async Task Model_AfterMigrations_HasNoPendingChanges()
    {
        await using var dbContext = _database.CreateDbContext();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
        Assert.False(dbContext.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("amount", "0", "ck_subscriptions_amount_positive")]
    [InlineData("amount", "-5", "ck_subscriptions_amount_positive")]
    [InlineData("owner_id", "''", "ck_subscriptions_owner_id_not_empty")]
    [InlineData("billing_cycle", "'Daily'", "ck_subscriptions_billing_cycle")]
    [InlineData("status", "'Paused'", "ck_subscriptions_status")]
    public async Task Insert_InvalidSubscription_IsRejectedByCheckConstraint(string column, string value, string constraint)
    {
        // SQL directo: se salta el dominio para comprobar que la base de datos también protege la invariante.
        var values = new Dictionary<string, string>
        {
            ["id"] = "gen_random_uuid()",
            ["owner_id"] = "'it|owner-a'",
            ["name"] = "'Netflix'",
            ["amount"] = "10",
            ["currency"] = "'COP'",
            ["billing_cycle"] = "'Monthly'",
            ["next_billing_date"] = "DATE '2026-11-01'",
            ["status"] = "'Active'",
            ["created_at"] = "now()",
            ["updated_at"] = "now()",
        };
        values[column] = value;
        var sql = $"INSERT INTO subscriptions ({string.Join(", ", values.Keys)}) VALUES ({string.Join(", ", values.Values)})";

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_NegativeUsageCount_IsRejected()
    {
        var subscription = await SeedSubscriptionAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            $"INSERT INTO usage_records (id, subscription_id, usage_date, usage_count) " +
            $"VALUES (gen_random_uuid(), '{subscription.Id}', DATE '2026-10-10', -1)"));

        Assert.Equal("ck_usage_records_usage_count_non_negative", exception.ConstraintName);
    }

    [Fact]
    public async Task Insert_UsageForMissingSubscription_IsRejectedByForeignKey()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO usage_records (id, subscription_id, usage_date, usage_count) " +
            $"VALUES (gen_random_uuid(), '{Guid.NewGuid()}', DATE '2026-10-10', 1)"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task DeleteSubscription_CascadesUsageRecords()
    {
        var subscription = await SeedSubscriptionAsync();
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.UsageRecords.Add(UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 10), 3, null));
            dbContext.UsageRecords.Add(UsageRecord.Create(subscription.Id, new DateOnly(2026, 10, 11), 2, null));
            await dbContext.SaveChangesAsync();
        }

        await ExecuteAsync($"DELETE FROM subscriptions WHERE id = '{subscription.Id}'");

        Assert.Equal(["0"], await QueryAsync("SELECT count(*)::text FROM usage_records"));
    }

    private async Task<Subscription> SeedSubscriptionAsync()
    {
        var subscription = Subscription.Create(
            "it|owner-a", "Netflix", null, 38900m, "COP", BillingCycle.Monthly,
            new DateOnly(2026, 11, 1), new FixedTimeProvider(FixedNow));
        await using var dbContext = _database.CreateDbContext();
        dbContext.Subscriptions.Add(subscription);
        await dbContext.SaveChangesAsync();
        return subscription;
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
