using Microsoft.EntityFrameworkCore;
using TransactionLedger.Domain;

namespace TransactionLedger.Data;

/// <summary>
/// Registered Scoped (the AddDbContext default): one instance per HTTP
/// request, which matches the request-scoped unit of work every service
/// method needs. A Singleton DbContext would be shared across concurrent
/// requests, and DbContext is not thread-safe — two requests mutating the
/// same change tracker at once corrupts state silently.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
