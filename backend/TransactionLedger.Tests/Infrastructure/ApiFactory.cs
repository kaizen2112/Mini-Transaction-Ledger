using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TransactionLedger.Tests.Infrastructure;

/// <summary>
/// The real ASP.NET Core host, pointed at the Testcontainers Postgres
/// instance, running the real migrations on startup
/// (docs/07-testing-strategy.md §3).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-signing-key-that-is-long-enough-32+";
    public const string JwtIssuer = "transaction-ledger-tests";
    public const string JwtAudience = "transaction-ledger-tests-web";

    /// <summary>Records the SQL EF sends, for H8 (BR-40).</summary>
    public SqlCapture SqlCapture { get; } = new();

    private readonly string _connectionString;

    public ApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Added last, so it wins over appsettings.json and user-secrets.
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["Database:RunMigrationsOnStartup"] = "true",
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                // EF logs command text at Information; SqlCapture reads it.
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Information"
            });
        });

        builder.ConfigureLogging(logging => logging.AddProvider(SqlCapture));

        builder.ConfigureTestServices(services =>
        {
            services.AddControllers()
                .AddApplicationPart(typeof(TestProtectedController).Assembly);
        });
    }
}
