using FlowTrim.Domain.Subscriptions;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Carga todas las IEntityTypeConfiguration de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowTrimDbContext).Assembly);
    }
}
