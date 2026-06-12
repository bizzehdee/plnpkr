using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PlanningPoker.Api;
using PlanningPoker.Api.Health;
using PlanningPoker.Api.Hubs;
using PlanningPoker.Core;
using PlanningPoker.Core.Integrations;
using PlanningPoker.Core.Security;
using PlanningPoker.Data;
using PlanningPoker.Data.Sqlite;
using PlanningPoker.Data.SqlServer;
using PlanningPoker.Data.PostgreSql;
using PlanningPoker.Integrations;

namespace PlanningPoker.Api;

/// <summary>
/// Application entry point. Authored as an explicit <c>Program</c> class with a static <c>Main</c>
/// (rather than top-level statements). Kept a non-static class so the integration tests can use
/// <c>WebApplicationFactory&lt;Program&gt;</c> (a static class can't be a generic type argument).
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- Services -------------------------------------------------------

        // SQLite via EF Core. Connection string defaults to a local file for dev; on Azure App Service
        // set "ConnectionStrings__Default" to a path under persistent storage, e.g.
        //   Linux:   Data Source=/home/data/planningpoker.db
        //   Windows: Data Source=%HOME%/data/planningpoker.db
        // See #19/#3.
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? "Data Source=planningpoker.db";
        connectionString = Environment.ExpandEnvironmentVariables(connectionString);

        // Configurable database provider (#19): SQLite (default) | SqlServer | PostgreSql, each a
        // self-contained project (own driver + migrations). The composition root lists the available
        // providers; DatabaseConfiguration.Select picks the configured one and Configure wires the right
        // driver + migrations assembly. Provider-specific setup (SQLite dir + WAL) lives in the provider.
        var providerKind = DatabaseConfiguration.ParseProvider(builder.Configuration["Database:Provider"]);
        var availableProviders = new IDatabaseProvider[]
        {
            new SqliteDatabaseProvider(),
            new SqlServerDatabaseProvider(),
            new PostgreSqlDatabaseProvider(),
        };
        var database = DatabaseConfiguration.Select(providerKind, availableProviders);
        builder.Services.AddDbContext<PlanningPokerDbContext>(options => database.Configure(options, connectionString));

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IShortCodeGenerator, ShortCodeGenerator>();
        builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        builder.Services.AddScoped<ISessionStore, EfSessionStore>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<SessionMaintenanceService>();
        builder.Services.AddSingleton<ConnectionRegistry>();
        builder.Services.AddSingleton<ReactionRateLimiter>();
        builder.Services.AddHostedService<SessionEvictionService>();
        builder.Services.AddHostedService<RoundTimerService>(); // expires round timers → auto-reveal (#14)

        // --- Issue-tracker integration (#4, optional, off unless configured) ---
        var integrationOptions = new IntegrationsOptions
        {
            Jira = new() { Enabled = builder.Configuration.GetValue("Integrations:Jira:Enabled", false) },
            Ado = new() { Enabled = builder.Configuration.GetValue("Integrations:Ado:Enabled", false) },
        };
        builder.Services.AddSingleton(integrationOptions);

        var allowedHosts = builder.Configuration.GetSection("Integrations:AllowedHosts").Get<string[]>();
        builder.Services.AddHttpClient(); // IHttpClientFactory for the tracker adapters
        builder.Services.AddSingleton<ITrackerHostPolicy>(_ => new TrackerHostPolicy(allowedHosts));
        builder.Services.AddSingleton<IHtmlDescriptionSanitizer, HtmlDescriptionSanitizer>();
        builder.Services.AddSingleton<IIssueTracker, JiraIssueTracker>();
        builder.Services.AddSingleton<IIssueTracker, AzureDevOpsIssueTracker>();
        builder.Services.AddSingleton<IIssueTrackerFactory, IssueTrackerFactory>();
        builder.Services.AddSingleton<IIntegrationConnectionStore, InMemoryIntegrationConnectionStore>();
        builder.Services.AddSingleton<IBoardUrlParser, BoardUrlParser>();
        builder.Services.AddScoped<IntegrationService>();

        // OAuth "log in" flow (#4). Configured per provider via Integrations:{Provider}:OAuth.
        var jiraOAuth = builder.Configuration.GetSection("Integrations:Jira:OAuth").Get<OAuthProviderConfig>()
            ?? new OAuthProviderConfig();
        builder.Services.AddSingleton<IOAuthFlow>(sp =>
            new AtlassianOAuthFlow(sp.GetRequiredService<IHttpClientFactory>(), jiraOAuth));
        builder.Services.AddSingleton<IOAuthFlowProvider, OAuthFlowProvider>();
        builder.Services.AddSingleton<IOAuthFlowStore, InMemoryOAuthFlowStore>();
        builder.Services.AddScoped<OAuthService>();

        // Serialize enums as their names (e.g. "Fibonacci", "Observer") over both SignalR and REST so
        // the TypeScript client works with readable string unions rather than magic numbers.
        builder.Services.AddSignalR().AddJsonProtocol(options =>
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // MVC controllers host the REST surface (GET /api/sessions, /api/integrations/*). Enums serialize
        // as names here too, matching the SignalR protocol and the TypeScript string-union client. #9.
        builder.Services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Health checks (#3). The database check is tagged "ready" so liveness can exclude it.
        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

        // CORS. The bundled single-artifact deploy serves the SPA same-origin and needs none. Two
        // cross-origin cases do: the `ng serve` dev server (:4200), and a SPLIT deploy where the SPA is
        // hosted separately (blob/S3/CDN) — its origin(s) come from config `Cors:AllowedOrigins`.
        // SignalR requires explicit origins + AllowCredentials (no wildcard).
        const string SpaCorsPolicy = "SpaCors";
        var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (builder.Environment.IsDevelopment())
        {
            corsOrigins = corsOrigins.Concat(["http://localhost:4200", "https://localhost:4200"]).ToArray();
        }
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(SpaCorsPolicy, policy =>
            {
                if (corsOrigins.Length > 0)
                {
                    policy.WithOrigins(corsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials(); // required for SignalR
                }
            });
        });

        var app = builder.Build();

        // --- Migrate database at startup -----------------------------------
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanningPokerDbContext>();
            db.Database.Migrate();
            database.OnMigrated(db); // provider-specific post-migrate (e.g. SQLite WAL). See #19.
        }

        // --- Middleware / endpoints ----------------------------------------
        // Applied before controllers/hub. No-op for same-origin requests; serves the dev server and any
        // configured split-deploy SPA origins.
        app.UseCors(SpaCorsPolicy);

        // Serve the built Angular app from wwwroot (production) with SPA deep-link fallback.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Readiness: verifies the database is reachable + queryable (App Service hits this path).
        // Returns 200 when healthy, 503 otherwise. See #3.
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponse.WriteAsync });

        // Liveness: process is up and serving — no dependency checks (predicate runs no checks).
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponse.WriteAsync,
        });

        // REST endpoints live in MVC controllers (SessionsController, IntegrationsController). See #9.
        app.MapControllers();

        app.MapHub<PlanningPokerHub>("/hubs/poker");

        // SPA fallback: any unmatched non-API route returns index.html so Angular can route it.
        app.MapFallbackToFile("index.html");

        app.Run();
    }
}
