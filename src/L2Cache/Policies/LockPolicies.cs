using System.Diagnostics;
using L2Cache.Abstractions.Policies;
using L2Cache.Internal;

namespace L2Cache.Policies;

/// <summary>
/// 进程内内存锁策略（防止单机缓存击穿）。
/// </summary>
public sealed class MemoryLockPolicy : ILockPolicy
{
    private readonly AsyncKeyedLocker<string> _locker = new();
    private readonly TimeSpan _timeout;

    public MemoryLockPolicy(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public async ValueTask<ICacheLockHandle?> AcquireAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var releaser = await _locker.LockAsync(resourceKey, _timeout, cancellationToken).ConfigureAwait(false);
            return new Handle(releaser);
        }
        catch (TimeoutException)
        {
            // 超时降级：未获取到锁但允许继续执行（可用性优先）
            return new Handle(null);
        }
    }

    private sealed class Handle(IDisposable? releaser) : ICacheLockHandle
    {
        public bool Acquired => releaser != null;

        public ValueTask DisposeAsync()
        {
            releaser?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// 分布式锁策略（防止跨节点缓存击穿）。基于 L2 存储的锁原语实现。
/// <para>获取失败时以指数退避自旋重试，超时后降级为无锁继续执行。</para>
/// </summary>
public sealed class DistributedLockPolicy : ILockPolicy
{
    private readonly Abstractions.Stores.IL2CacheStore _store;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _expiry;

    public DistributedLockPolicy(Abstractions.Stores.IL2CacheStore store, TimeSpan timeout, TimeSpan expiry)
    {
        _store = store;
        _timeout = timeout;
        _expiry = expiry;
    }

    public async ValueTask<ICacheLockHandle?> AcquireAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        var lockKey = $"lock:{resourceKey}";
        var token = Guid.NewGuid().ToString("N");
        var start = Stopwatch.GetTimestamp();
        var retryDelay = 20;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _store.AcquireLockAsync(lockKey, token, _expiry, cancellationToken).ConfigureAwait(false))
            {
                return new Handle(_store, lockKey, token);
            }

            if (Stopwatch.GetElapsedTime(start) > _timeout)
            {
                // 超时降级：其他节点正在加载，直接回源（可用性优先）
                return new Handle(null, null, null);
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            retryDelay = Math.Min(retryDelay * 2, 200);
        }
    }

    private sealed class Handle(Abstractions.Stores.IL2CacheStore? store, string? lockKey, string? token) : ICacheLockHandle
    {
        public bool Acquired => lockKey != null;

        public async ValueTask DisposeAsync()
        {
            if (store != null && lockKey != null && token != null)
            {
                await store.ReleaseLockAsync(lockKey, token).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// 组合锁策略：先获取内存锁（拦截本机并发），再获取分布式锁（拦截跨节点并发）。
/// 任一环节失败即整体降级。
/// </summary>
public sealed class ChainedLockPolicy : ILockPolicy
{
    private readonly ILockPolicy _first;
    private readonly ILockPolicy _second;

    public ChainedLockPolicy(ILockPolicy first, ILockPolicy second)
    {
        _first = first;
        _second = second;
    }

    public async ValueTask<ICacheLockHandle?> AcquireAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        var firstHandle = await _first.AcquireAsync(resourceKey, cancellationToken).ConfigureAwait(false);
        if (firstHandle is not { Acquired: true })
        {
            // 第一级降级或不可用，不再争抢第二级
            return firstHandle;
        }

        try
        {
            var secondHandle = await _second.AcquireAsync(resourceKey, cancellationToken).ConfigureAwait(false);
            if (secondHandle is not { Acquired: true })
            {
                // 第二级降级：整体降级（释放第一级）
                await firstHandle.DisposeAsync().ConfigureAwait(false);
                return secondHandle;
            }

            return new Handle(firstHandle, secondHandle);
        }
        catch
        {
            await firstHandle.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Handle(ICacheLockHandle first, ICacheLockHandle second) : ICacheLockHandle
    {
        public bool Acquired => true;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await second.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await first.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
