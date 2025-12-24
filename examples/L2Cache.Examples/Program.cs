using L2Cache;
using L2Cache.Extensions;
using L2Cache.Examples.Services;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configure OpenTelemetry
var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:5081";
var otelHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"];

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("L2Cache.Examples"))
            .AddAspNetCoreInstrumentation()
            .AddMeter("L2Cache") // Subscribe to L2Cache metrics
            .AddOtlpExporter(options => 
            {
                options.Endpoint = new Uri(otelEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
                if (!string.IsNullOrEmpty(otelHeaders))
                {
                    options.Headers = otelHeaders;
                }
            });
    })
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("L2Cache.Examples"))
            .AddAspNetCoreInstrumentation()
            .AddSource("L2Cache") // Subscribe to L2Cache activities
            .AddOtlpExporter(options => 
            {
                options.Endpoint = new Uri(otelEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
                if (!string.IsNullOrEmpty(otelHeaders))
                {
                    options.Headers = otelHeaders;
                }
            });
    });

// Configure L2Cache (replaces AddRedisCacheService)
builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;
    options.UseRedis = true;
    options.Redis.ConnectionString = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379") + ",abortConnect=false";
    
    // Enable Metrics
    options.Telemetry.MetricsPrefix = "l2cache";
    options.Telemetry.ActivitySourceName = "L2Cache";

    // Enable Health Check
    options.Telemetry.EnableHealthCheck = true;

    // Enable Background Refresh
    options.BackgroundRefresh.Enabled = true;
    options.BackgroundRefresh.Interval = TimeSpan.FromMinutes(1);
}).AddL2CacheTelemetry();

// Register Custom Services
// GenericCacheService was removed in favor of using the default L2CacheService directly (as shown in BasicsController)
// ProductCacheService demonstrates inheriting L2CacheService for custom behavior (Cache Aside, etc.)
builder.Services.AddScoped<ProductCacheService>();

// CustomUserCacheService demonstrates inheriting AbstractCacheService directly
builder.Services.AddScoped<CustomUserCacheService>();

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "L2Cache Examples API",
        Version = "v1",
        Description = "Comprehensive examples for L2Cache usage including Basics, Entity Caching, and Advanced Scenarios."
    });

    // XML Comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Configure Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "L2Cache Examples API V1");
        c.RoutePrefix = string.Empty; // Swagger at root
    });
}

app.UseAuthorization();
app.MapControllers();

// Health Check
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Redirect root to Swagger
app.MapGet("/", () => Results.Redirect("/swagger"));

// Startup Log
app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("L2Cache Examples API Started");
    logger.LogInformation("Swagger UI: http://localhost:5000/swagger");
    logger.LogInformation("Basics: http://localhost:5000/api/basics/test-key");
    logger.LogInformation("Products: http://localhost:5000/api/product/1001");
});

app.Run();

public partial class Program { }
