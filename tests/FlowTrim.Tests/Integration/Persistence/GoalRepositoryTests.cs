using FlowTrim.Application.Goals;
using FlowTrim.Domain.Goals;
using FlowTrim.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FlowTrim.Tests.Integration.Persistence;

/// <summary>EP-15 HU-77 contra PostgreSQL: totales sumados en SQL, cascada y FK compuesta por propietario.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GoalRepositoryTests : IAsyncLifetime
{
    private const string OwnerA = "it|owner-a";
    private const string OwnerB = "it|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    private readonly PostgresFixture _database;
    private readonly ServiceProvider _services;

    public GoalRepositoryTests(PostgresFixture database)
    {
        _database = database;
        _services = database.CreateInfrastructureServices();
    }

    public Task InitializeAsync() => _database.ResetAsync();

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Totals_SumEverythingAndTheAveragePeriodPerGoal_OnlyForTheOwner()
    {
        var trip = SavingsGoal.Create(OwnerA, "Trip", "COP", 1000m, null, Clock);
        var empty = SavingsGoal.Create(OwnerA, "Empty", "COP", 1000m, null, Clock);
        var foreign = SavingsGoal.Create(OwnerB, "Trip", "COP", 1000m, null, Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.AddRange(trip, empty, foreign);
            dbContext.AddRange(
                Contribution(OwnerA, trip, 100m, 6, 30),
                Contribution(OwnerA, trip, 200m, 7, 1),
                Contribution(OwnerA, trip, -50m, 9, 30),
                Contribution(OwnerA, trip, 400m, 10, 1),
                Contribution(OwnerB, foreign, 999m, 8, 1));
            await dbContext.SaveChangesAsync();
        }

        using var scope = _services.CreateScope();
        var totals = await scope.ServiceProvider.GetRequiredService<IGoalRepository>()
            .GetTotalsAsync(OwnerA, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), default);

        Assert.Equal(new GoalTotals(trip.Id, 650m, 150m, 4), Assert.Single(totals).Value);
    }

    [Fact]
    public async Task DeleteGoal_DeletesItsContributions_AndForeignGoalsAreRejected()
    {
        var trip = SavingsGoal.Create(OwnerA, "Trip", "COP", 1000m, null, Clock);
        var foreign = SavingsGoal.Create(OwnerB, "Trip", "COP", 1000m, null, Clock);
        await using (var dbContext = _database.CreateDbContext())
        {
            dbContext.AddRange(trip, foreign, Contribution(OwnerA, trip, 100m, 10, 1));
            await dbContext.SaveChangesAsync();
        }

        await using (var crossOwner = _database.CreateDbContext())
        {
            crossOwner.Add(Contribution(OwnerA, foreign, 5m, 10, 1));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => crossOwner.SaveChangesAsync());
            Assert.Equal("fk_goal_contributions_savings_goals_owner_id_goal_id",
                Assert.IsType<PostgresException>(exception.InnerException).ConstraintName);
        }

        using (var scope = _services.CreateScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IGoalRepository>().DeleteAsync(OwnerA, trip.Id, default));
        }

        await using var check = _database.CreateDbContext();
        Assert.Empty(await check.GoalContributions.ToListAsync());
    }

    private static GoalContribution Contribution(string owner, SavingsGoal goal, decimal amount, int month, int day) =>
        GoalContribution.Create(owner, goal.Id, amount, new DateOnly(2026, month, day), null, Clock);
}
