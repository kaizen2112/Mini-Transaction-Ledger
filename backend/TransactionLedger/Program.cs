using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TransactionLedger.Configuration;
using TransactionLedger.Data;
using TransactionLedger.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Controllers, not minimal APIs: the HTTP surface stays declarative and
// attribute-routed, and every endpoint has one obvious home on disk.
builder.Services.AddControllers();

// Strongly typed, validated at startup (ValidateOnStart). A Jwt:Key under
// 32 characters or a missing connection string crashes the app at boot,
// not on the first login or first query (docs/08-docker.md §6).
builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DatabaseSettings>()
    .Configure(options =>
    {
        options.ConnectionString = builder.Configuration.GetConnectionString("Default") ?? string.Empty;
        options.RunMigrationsOnStartup = builder.Configuration.GetValue<bool>("Database:RunMigrationsOnStartup");
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var dbSettings = serviceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
    options.UseNpgsql(dbSettings.ConnectionString);
});

builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// Swagger, Development only (contract §10). Bearer security definition so
// the Authorize button lets an evaluator exercise protected endpoints.
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Transaction Ledger API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter a valid JWT bearer token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Pipeline order (one-line reason each):
//   1. GlobalExceptionMiddleware - outermost, so any throw below becomes
//      problem+json with a traceId and never a stack trace (BR-45/BR-47)
//   2. Swagger, Development only - a documentation surface, not a protected
//      endpoint, so it sits above auth deliberately
//   3. UseRouting - selects the endpoint before anything below can inspect it
//   4. UseAuthentication / UseAuthorization - arrive in B4; nothing to
//      enforce yet, so they are not called here
//   5. MapControllers - terminal
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.MapControllers();

// Guarded by config: local `dotnet run` defaults this to false so a
// developer without Postgres running yet does not crash-loop; Docker
// Compose sets Database__RunMigrationsOnStartup=true explicitly, after the
// db healthcheck has already passed (docs/08-docker.md §5/§7). A no-op
// until Step 6 adds the first migration.
using (var scope = app.Services.CreateScope())
{
    var dbSettings = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
    if (dbSettings.RunMigrationsOnStartup)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}

app.Run();
