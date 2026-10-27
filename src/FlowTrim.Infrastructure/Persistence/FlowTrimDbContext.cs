using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Budgets;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Goals;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Transactions;
using FlowTrim.Domain.Usage;
using Microsoft.EntityFrameworkCore;

namespace FlowTrim.Infrastructure.Persistence;

/// <summary>Contexto EF Core de FlowTrim. El mapeo snake_case se aplica en la configuración de DI.</summary>
public sealed class FlowTrimDbContext : DbContext
{
    public FlowTrimDbContext(DbContextOptions<FlowTrimDbContext> options) : base(options)
    {
    }

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Carga todas las IEntityTypeConfiguration de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowTrimDbContext).Assembly);
    }
}
