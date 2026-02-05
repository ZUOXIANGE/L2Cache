using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace L2Cache.Tests.Integration.Fixtures;

public class IntegrationTestFactory : IDisposable
{
    private ServiceProvider? _serviceProvider;
    public string RedisConnectionString { get; set; } = string.Empty;

    public IServiceProvider Services
    {
        get
        {
            if (_serviceProvider == null)
            {
                _serviceProvider = CreateServiceProvider();
            }
            return _serviceProvider;
        }
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole());

        services.AddL2Cache(options =>
        {
            options.UseLocalCache = true;
            options.UseRedis = true;
            options.Redis.ConnectionString = RedisConnectionString;
        });

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
            _serviceProvider = null;
        }
        GC.SuppressFinalize(this);
    }
}
