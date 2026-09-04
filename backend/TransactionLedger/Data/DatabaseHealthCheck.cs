using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TransactionLedger.Data;

/// <summary>
/// Backs GET /health (contract §2, test K7). Uses CanConnectAsync rather than
/// a package like AspNetCore.HealthChecks.NpgSql — EF Core already knows how
/// to open a connection, and one probe query does not need a whole library.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _dbContext;

    public DatabaseHealthCheck(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
    }
}
