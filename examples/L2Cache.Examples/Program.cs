using L2Cache;
using L2Cache.Abstractions.Serialization;
using L2Cache.Examples.Models;
using L2Cache.Examples.Services;
using L2Cache.Extensions;
using L2Cache.Serializers.MemoryPack;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

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

// Configure L2Cache
var l2Cache = builder.Services.AddL2Cache(options =>
{
    options.UseLocalCache = true;
    options.UseRedis = true;
    options.Redis.ConnectionString = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379") + ",abortConnect=false";

    // Enable Metrics
    options.Telemetry.MetricsPrefix = "l2cache";
    options.Telemetry.ActivitySourceName = "L2Cache";

    // Background Refresh 全局默认间隔（区域可通过 WithBackgroundRefresh 覆盖）
    options.BackgroundRefresh.Enabled = true;
    options.BackgroundRefresh.Interval = TimeSpan.FromMinutes(1);
});

// Basics：最简单的 string -> string 区域，无需 Loader
l2Cache.AddCache<string, string>("basics");

// Products：演示 Loader 回源 + 后台刷新
l2Cache.AddCache<int, ProductDto>("products", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(10);
})
    .WithLoader<ProductLoader>()
    .WithBackgroundRefresh(refresh => refresh.Interval = TimeSpan.FromMinutes(1));

// Users：演示 LoaderBase（只实现单条查询，批量逐 Key 回源）
l2Cache.AddCache<int, UserDto>("users", region =>
{
    region.DefaultTtl = TimeSpan.FromMinutes(10);
})
    .WithLoader<CustomUserLoader>();

// 序列化器：全局注册 MemoryPack 实现（默认 JSON，可替换为任意 ICacheSerializer 实现）
//builder.Services.AddSingleton<ICacheSerializer>(new MemoryPackCacheSerializer());

// 遥测：将默认的 NoOpTelemetryProvider 替换为 DefaultTelemetryProvider（指标/追踪/统计）
builder.Services.AddL2CacheTelemetry();

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "L2Cache Examples API";
        document.Info.Version = "v1";
        document.Info.Description = "Comprehensive examples for L2Cache usage including Basics, Entity Caching, and Advanced Scenarios.";
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Configure Pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("L2Cache Examples API");
        options.WithTheme(ScalarTheme.Moon);
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseAuthorization();
app.MapControllers();

// Redirect root to Scalar
app.MapGet("/", () => Results.Redirect("/scalar/v1"));

// Startup Log
app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("L2Cache Examples API Started");
    logger.LogInformation("Scalar UI: http://localhost:5000/scalar/v1");
    logger.LogInformation("Basics: http://localhost:5000/api/basics/test-key");
    logger.LogInformation("Products: http://localhost:5000/api/product/1001");
});

app.Run();

public partial class Program { }
