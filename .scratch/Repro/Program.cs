using L2Cache.Abstractions;
using L2Cache.Abstractions.Policies;
using L2Cache.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

// 启动临时 Redis 由外部脚本传入连接串；这里固定使用本地 docker run
var connStr = args.Length > 0 ? args[0] : "localhost:6379";

var services = new ServiceCollection();
services.AddLogging();
services.AddL2Cache(o =>
{
    o.UseLocalCache = true;
    o.UseRedis = true;
    o.Redis.ConnectionString = connStr;
}).AddCache<string, string>("it_repro", region =>
{
    region.NullValue.Enabled = true;
    region.NullValue.Ttl = TimeSpan.FromSeconds(5);
}).WithLoader(_ => new TestLoader());

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var client = scope.ServiceProvider.GetRequiredService<ICacheClient<string, string>>();

var result1 = await client.GetOrLoadAsync("null_key_1");
Console.WriteLine($"[1] result1={(result1 ?? "(null)")}");

using var redis = ConnectionMultiplexer.Connect(connStr);
var db = redis.GetDatabase();
var val = await db.StringGetAsync("it_repro:null_key_1");
Console.WriteLine($"[1] redis value={(val.HasValue ? val.ToString() : "(missing)")} ttl={(await db.KeyTimeToLiveAsync("it_repro:null_key_1"))?.TotalSeconds}");

var result2 = await client.GetOrLoadAsync("null_key_1");
Console.WriteLine($"[2] result2={(result2 ?? "(null)")} loadCount={TestLoader.Count}");

// 分布式锁验证
var descriptorLockWorks = await TestLockAsync(db);
Console.WriteLine($"[3] LockTake/Release works: {descriptorLockWorks}");

// 并发击穿验证
var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => client.GetOrLoadAsync("stampede_1")));
var results = await Task.WhenAll(tasks);
Console.WriteLine($"[4] stampede loadCount={TestLoader.Count - TestLoader.CountAtStampedeStart}, results all equal: {results.All(r => r == results[0])}");

static async Task<bool> TestLockAsync(IDatabase db)
{
    var taken = await db.LockTakeAsync("it_repro:lock_probe", "token1", TimeSpan.FromSeconds(10));
    var takenAgain = await db.LockTakeAsync("it_repro:lock_probe", "token2", TimeSpan.FromSeconds(10));
    var released = await db.LockReleaseAsync("it_repro:lock_probe", "token1");
    return taken && !takenAgain && released;
}

internal static class TestLoader
{
    public static int Count;
    public static int CountAtStampedeStart;

    static TestLoader() => CountAtStampedeStart = int.MinValue;
}

internal sealed class TestLoader : ILoader<string, string>
{
    public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref TestLoader.Count);
        if (key == "stampede_1")
        {
            // 记录击穿开始时的计数（首次进入时）
            if (TestLoader.CountAtStampedeStart == int.MinValue)
            {
                TestLoader.CountAtStampedeStart = TestLoader.Count - 1;
            }

            return Task.FromResult<string?>("db_stampede_1");
        }

        return Task.FromResult<string?>(null);
    }

    public Task<Dictionary<string, string>> LoadManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        => Task.FromResult(new Dictionary<string, string>());
}
