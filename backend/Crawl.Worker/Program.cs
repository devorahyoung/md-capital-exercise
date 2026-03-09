using System.Text.Json;
using Crawl.Worker;
using Crawl.Worker.Consumers;
using Crawl.Worker.Data;
using MassTransit;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// ── Crawler options ────────────────────────────────────────────────────────────
builder.Services.Configure<CrawlerOptions>(
    builder.Configuration.GetSection(CrawlerOptions.SectionName));

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<WorkerDbContext>(opt =>
    opt.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null)));

// ── HttpClient with Polly retry (3 retries, exponential back-off) ─────────────
// SSL validation is intentionally bypassed: a crawler visits arbitrary third-party
// sites whose certificates may not be trusted by the container's OS cert store.
builder.Services.AddHttpClient("CrawlClient", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (compatible; CrawlBot/1.0)");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            onRetry: (outcome, delay, attempt, _) =>
            {
                Console.WriteLine(
                    $"[Polly] Retry {attempt} after {delay.TotalSeconds:F1}s — " +
                    $"{outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
            }));

// ── MassTransit / RabbitMQ ────────────────────────────────────────────────────
builder.Services.AddMassTransit(x =>
{
    // Register consumer together with its definition, which declares the
    // explicit retry schedule and pins the DLQ name to "StartCrawlJob_error".
    x.AddConsumer<StartCrawlJobConsumer, StartCrawlJobConsumerDefinition>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(ctx);
    });
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<WorkerDbContext>("database", tags: ["ready"])
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

// Bind the HTTP listener only to the health-check port
builder.WebHost.UseUrls("http://*:8081");

var app = builder.Build();

var jsonHealthWriter = (HttpContext ctx, HealthReport report) =>
{
    ctx.Response.ContentType = "application/json";
    return ctx.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description
        })
    }));
};

// GET /health        — liveness  (self only)
// GET /health/ready  — readiness (self + database)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = jsonHealthWriter
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = jsonHealthWriter
});

app.Run();
