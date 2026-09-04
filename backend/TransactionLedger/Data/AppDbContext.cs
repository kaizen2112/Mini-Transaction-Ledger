using Microsoft.EntityFrameworkCore;

namespace TransactionLedger.Data;

/// <summary>
/// No DbSets yet — the first entity (Users) arrives in Step 6. Registered
/// Scoped (the AddDbContext default): one instance per HTTP request, which
/// matches the request-scoped unit-of-work every service method in this
/// project needs. A Singleton DbContext would be shared across concurrent
/// requests, and DbContext is not thread-safe — two requests mutating the
/// same change tracker at once corrupts state silently.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
