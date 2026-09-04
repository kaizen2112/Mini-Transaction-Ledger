using System.ComponentModel.DataAnnotations;

namespace TransactionLedger.Configuration;

/// <summary>
/// Both properties are set via a single Configure lambda in Program.cs rather
/// than section Bind(), because they come from two different configuration
/// shapes: ConnectionString from ConnectionStrings:Default (the ASP.NET Core
/// convention, ConnectionStrings__Default in docker-compose.yml) and
/// RunMigrationsOnStartup from its own Database section (docs/08-docker.md
/// §5/§7).
/// </summary>
public sealed class DatabaseSettings
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public bool RunMigrationsOnStartup { get; set; }
}
