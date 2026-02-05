using Ductus.FluentDocker.Builders;
using Ductus.FluentDocker.Services;

namespace L2Cache.Benchmarks;

public class RedisContainer : IDisposable
{
    private IContainerService? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public void Start()
    {
        _container = new Builder()
            .UseContainer()
            .UseImage("redis:latest")
            .ExposePort(6379)
            .WaitForPort("6379/tcp", 30000)
            .Build()
            .Start();

        var config = _container.GetConfiguration(true);
        if (config.NetworkSettings.Ports.TryGetValue("6379/tcp", out var bindings) && bindings.Length > 0)
        {
            ConnectionString = $"127.0.0.1:{bindings[0].HostPort}";
        }
        else
        {
            throw new InvalidOperationException("Redis port 6379 not mapped.");
        }
    }

    public void Dispose()
    {
        if (_container != null)
        {
            try
            {
                _container.Stop();
                // _container.Remove(force: true);
            }
            catch
            {
                // ignore errors during cleanup
            }
            finally
            {
                _container.Dispose();
            }
        }
        GC.SuppressFinalize(this);
    }
}
