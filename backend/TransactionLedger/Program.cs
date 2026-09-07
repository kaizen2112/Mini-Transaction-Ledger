using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using TransactionLedger.Configuration;
using TransactionLedger.Data;
using TransactionLedger.Domain;
using TransactionLedger.Middleware;
using TransactionLedger.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers, not minimal APIs: the HTTP surface stays declarative and
// attribute-routed, and every endpoint has one obvious home on disk.
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Contract §1.3: enums serialise as their NAMES in both directions.
        // The integer storage in docs/04 §1.4 is an implementation detail the
        // API does not leak.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

        // Contract Format header: money is a JSON number with two decimals.
        options.JsonSerializerOptions.Converters.Add(new MoneyJsonConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // [ApiController]'s automatic 400 otherwise emits ASP.NET's own
        // ValidationProblemDetails shape, which has no `code` member. The
        // frontend switches on `code` (BR-45), so it must be present here too.
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

            var problem = ProblemResponse.Build(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "The request failed validation.",
                context.HttpContext.TraceIdentifier,
                errors);

            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { ProblemResponse.ContentType }
            };
        };
    });

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

builder.Services
    .AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var dbSettings = serviceProvider.GetRequiredService<IOptions<DatabaseSettings>>().Value;
    options.UseNpgsql(dbSettings.ConnectionString);
});

// PBKDF2 with a per-password salt and an embedded iteration count (BR-09).
// Stateless and thread-safe, so a singleton is correct.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtSettings) =>
    {
        var settings = jwtSettings.Value;

        // Keep `sub` as `sub`. Without this the handler helpfully rewrites it
        // to the long ClaimTypes.NameIdentifier URI and GetUserId() (BR-06)
        // silently finds nothing.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            // The handler's default 401 has an empty body. The contract says
            // every error is problem+json with a code (BR-45).
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await ProblemResponse.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    ErrorCodes.Unauthenticated,
                    "Authentication is required to access this resource.");
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors();

// Configured through IOptions rather than by reading builder.Configuration
// inline, matching the JwtBearerOptions block above: one pattern for every
// options type in this file.
builder.Services
    .AddOptions<CorsOptions>()
    .Configure<IOptions<CorsSettings>>((options, corsSettings) =>
    {
        options.AddPolicy(CorsSettings.PolicyName, policy => policy
            .WithOrigins(corsSettings.Value.AllowedOrigins)
            // GET and POST only: nothing in this API updates or deletes.
            // The ledger is append-only (BR-21/BR-22), so there is no PUT,
            // PATCH or DELETE to allow. OPTIONS is the preflight itself and
            // is handled by the middleware, not by this list.
            .WithMethods(HttpMethods.Get, HttpMethods.Post)
            // Idempotency-Key is a custom header, so it is NOT on the CORS
            // safelist and any request carrying it is preflighted. Omit it
            // here and every POST to /transactions and /transfers fails
            // before it reaches a controller.
            .WithHeaders(
                HeaderNames.Authorization,
                HeaderNames.ContentType,
                IdempotencyHeader.Name)
            // Response headers are hidden from JavaScript unless exposed.
            // The replay header ARRIVES either way; without this line fetch()
            // simply cannot see it, and the frontend cannot tell a replay
            // from a fresh create (BR-34).
            .WithExposedHeaders(IdempotencyHeader.ReplayName));
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
//   3. UseRouting - matches the endpoint and attaches its metadata, which
//      UseAuthorization below cannot read until it has run
//   4. UseCors - after routing so the endpoint's metadata exists, and before
//      authentication so a preflight OPTIONS (which carries no token) gets
//      its CORS headers back instead of a 401
//   5. UseAuthentication - turns the bearer token into a ClaimsPrincipal
//   6. UseAuthorization - enforces the matched endpoint's policy using that
//      principal; nothing to judge without step 5
//   7. MapControllers - terminal
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.UseCors(CorsSettings.PolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Guarded by config: local `dotnet run` defaults this to false so a
// developer without Postgres running yet does not crash-loop; Docker
// Compose sets Database__RunMigrationsOnStartup=true explicitly, after the
// db healthcheck has already passed (docs/08-docker.md §5/§7).
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

/// <summary>
/// Top-level statements compile to an internal Program class. This makes it
/// visible to WebApplicationFactory&lt;Program&gt; in the test project
/// (docs/07-testing-strategy.md §3).
/// </summary>
public partial class Program;
