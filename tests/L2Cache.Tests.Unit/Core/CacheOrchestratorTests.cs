using System.Collections.Concurrent;
using L2Cache.Abstractions.Policies;
using L2Cache.Abstractions.Stores;
using L2Cache.Configuration;
using L2Cache.Core;
using L2Cache.Policies;
using L2Cache.Serializers.Json;

namespace L2Cache.Tests.Unit.Core;

/// <summary>
/// CacheOrchestrator 直连测试：注入内存 L1 + 假 L2 存储，
/// 验证读管道（L2 命中回填 L1）、写管道（锁 + L2 + L1）与空值缓存
/// </summary>
public class CacheOrchestratorTests
{
    private static readonly JsonCacheSerializer Serializer = new();

    private static CacheDescriptor<int, string> CreateDescriptor(bool nullValueEnabled = true, bool withLock = true)
    {
        return new CacheDescriptor<int, string>
        {
            CacheName = "users",
            Options = new CacheRegionOptions
            {
                NullValue = new NullValueOptions { Enabled = nullValueEnabled, Ttl = TimeSpan.FromSeconds(30) }
            },
            KeyBuilder = new DefaultKeyBuilder<int>(),
            Expiry = new DefaultExpiryPolicy(new CacheRegionOptions { MaxL1Ttl = TimeSpan.FromMinutes(5) }),
            NullValue = new SentinelNullValuePolicy(new NullValueOptions
            {
                Enabled = nullValueEnabled,
                Ttl = TimeSpan.FromSeconds(30)
            }),
            Serializer = Serializer,
            Lock = withLock ? new MemoryLockPolicy(TimeSpan.FromSeconds(1)) : null
        };
    }

    [Test]
    public async Task GetAsync_L2Hit_ShouldBackfillL1()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        l2.Data["users:1"] = Serializer.Serialize("from-l2");
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor();

        var first = await orchestrator.GetAsync(descriptor, 1);

        // 移除 L2 数据后再次读取仍能命中，证明值已回填 L1
        l2.Data.Remove("users:1");
        var second = await orchestrator.GetAsync(descriptor, 1);

        await Assert.That(first.Status).IsEqualTo(CacheStatus.Found);
        await Assert.That(first.Value).IsEqualTo("from-l2");
        await Assert.That(second.Status).IsEqualTo(CacheStatus.Found);
        await Assert.That(second.Value).IsEqualTo("from-l2");
        await Assert.That(l1.Exists("users:1")).IsTrue();
    }

    [Test]
    public async Task GetAsync_AllMiss_ShouldReturnNotFound()
    {
        var orchestrator = new CacheOrchestrator(new FakeL1Store(), new FakeL2Store());
        var descriptor = CreateDescriptor();

        var result = await orchestrator.GetAsync(descriptor, 1);

        await Assert.That(result.IsNotFound).IsTrue();
    }

    [Test]
    public async Task GetAsync_L2NullSentinel_ShouldReturnFoundNullAndCacheNullInL1()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        l2.Data["users:404"] = "@@NULL@@"u8.ToArray();
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor();

        var result = await orchestrator.GetAsync(descriptor, 404);

        await Assert.That(result.IsFoundNull).IsTrue();
        await Assert.That(l1.Exists("users:404")).IsTrue();
        await Assert.That(l1.GetValue("users:404").IsNullValue).IsTrue();
    }

    [Test]
    public async Task PutAsync_ShouldWriteL2AndL1()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor();

        await orchestrator.PutAsync(descriptor, 1, "written");

        await Assert.That(l2.Data["users:1"]).IsEquivalentTo(Serializer.Serialize("written"));
        await Assert.That(l1.GetValue("users:1").Value).IsEqualTo("written");
    }

    [Test]
    public async Task EvictAsync_ShouldRemoveL1AndL2()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor();

        await orchestrator.PutAsync(descriptor, 1, "written");
        var removed = await orchestrator.EvictAsync(descriptor, 1);

        await Assert.That(removed).IsTrue();
        await Assert.That(l2.Data.ContainsKey("users:1")).IsFalse();
        await Assert.That(l1.Exists("users:1")).IsFalse();
    }

    [Test]
    public async Task GetOrLoadAsync_WhenLoaderReturnsNull_ShouldWriteNullSentinelToL2()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor();
        var loader = new DelegateLoader(_ => (string?)null);

        var first = await orchestrator.GetOrLoadAsync(descriptor, loader, 404);
        var second = await orchestrator.GetOrLoadAsync(descriptor, loader, 404);

        // 首次回源得到空值返回 NotFound；第二次命中空值缓存返回 FoundNull
        await Assert.That(first.IsNotFound).IsTrue();
        await Assert.That(second.IsFoundNull).IsTrue();
        await Assert.That(l2.Data["users:404"]).IsEquivalentTo("@@NULL@@"u8.ToArray());
        await Assert.That(loader.LoadCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrLoadAsync_WhenNullValueDisabled_ShouldNotCache()
    {
        var l1 = new FakeL1Store();
        var l2 = new FakeL2Store();
        var orchestrator = new CacheOrchestrator(l1, l2);
        var descriptor = CreateDescriptor(nullValueEnabled: false);
        var loader = new DelegateLoader(_ => (string?)null);

        await orchestrator.GetOrLoadAsync(descriptor, loader, 404);
        await orchestrator.GetOrLoadAsync(descriptor, loader, 404);

        await Assert.That(loader.LoadCount).IsEqualTo(2);
        await Assert.That(l2.Data.ContainsKey("users:404")).IsFalse();
    }

    [Test]
    public async Task MemoryLockPolicy_ShouldAcquireAndRelease()
    {
        var policy = new MemoryLockPolicy(TimeSpan.FromSeconds(1));

        var handle = await policy.AcquireAsync("users:1");
        await Assert.That(handle).IsNotNull();
        await Assert.That(handle!.Acquired).IsTrue();
        await handle.DisposeAsync();

        var again = await policy.AcquireAsync("users:1");
        await Assert.That(again!.Acquired).IsTrue();
        await again.DisposeAsync();
    }

    private sealed class FakeL1Store : IL1CacheStore
    {
        private sealed record Entry(object? Value, bool IsNull);

        private readonly ConcurrentDictionary<string, Entry> _data = new();

        public L1Entry GetValue(string key) =>
            _data.TryGetValue(key, out var entry)
                ? new L1Entry(true, entry.IsNull, entry.Value)
                : L1Entry.NotFound;

        public void SetValue(string key, object? value, TimeSpan? ttl) =>
            _data[key] = new Entry(value, value is null);

        public void Remove(string key) => _data.TryRemove(key, out _);

        public bool Exists(string key) => _data.ContainsKey(key);
    }

    private sealed class FakeL2Store : IL2CacheStore
    {
        public Dictionary<string, byte[]> Data { get; } = new();

        public Task<StoreEntry> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Data.TryGetValue(key, out var payload)
                    ? new StoreEntry(true, payload)
                    : StoreEntry.NotFound);
        }

        public Task<bool> SetAsync(string key, ReadOnlyMemory<byte> payload, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default)
        {
            if (onlyIfAbsent && Data.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            Data[key] = payload.ToArray();
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Data.Remove(key));

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(Data.ContainsKey(key));

        public Task<Dictionary<string, StoreEntry>> GetManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, StoreEntry>();
            foreach (var key in keys)
            {
                result[key] = Data.TryGetValue(key, out var payload)
                    ? new StoreEntry(true, payload)
                    : StoreEntry.NotFound;
            }

            return Task.FromResult(result);
        }

        public Task<HashSet<string>> SetManyAsync(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> items, TimeSpan? ttl, bool onlyIfAbsent = false, CancellationToken cancellationToken = default)
        {
            var written = new HashSet<string>();
            foreach (var (key, payload) in items)
            {
                if (onlyIfAbsent && Data.ContainsKey(key))
                {
                    continue;
                }

                Data[key] = payload.ToArray();
                written.Add(key);
            }

            return Task.FromResult(written);
        }

        public Task<long> RemoveManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            long removed = 0;
            foreach (var key in keys)
            {
                if (Data.Remove(key))
                {
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }

        public Task<bool> AcquireLockAsync(string lockKey, string token, TimeSpan expiry, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ReleaseLockAsync(string lockKey, string token, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class DelegateLoader : ILoader<int, string>
    {
        public DelegateLoader(Func<int, string?> loader) => _loader = loader;

        private readonly Func<int, string?> _loader;

        public int LoadCount;

        public Task<string?> LoadAsync(int key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref LoadCount);
            return Task.FromResult(_loader(key));
        }

        public Task<Dictionary<int, string>> LoadManyAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref LoadCount);
            return Task.FromResult(keys.Select(k => (Key: k, Value: _loader(k))).Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value!));
        }
    }
}
