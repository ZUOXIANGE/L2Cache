using System.Collections.Concurrent;

namespace L2Cache.Internal;

/// <summary>
/// 异步 Keyed 锁
/// <para>基于 SemaphoreSlim 实现的细粒度内存锁。</para>
/// </summary>
internal class AsyncKeyedLocker<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _semaphores = new();

    /// <summary>
    /// 获取并进入锁
    /// </summary>
    /// <param name="key">锁的 Key</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>释放锁的 Disposable 对象</returns>
    public async Task<IDisposable> LockAsync(TKey key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        
        var entered = await semaphore.WaitAsync(timeout, cancellationToken);
        if (!entered)
        {
            throw new TimeoutException($"Failed to acquire memory lock for key: {key}");
        }

        return new Releaser(key, this);
    }

    private void Release(TKey key)
    {
        if (_semaphores.TryGetValue(key, out var semaphore))
        {
            semaphore.Release();
            // 注意：这里没有做彻底的清理（例如当 waiting count 为 0 时移除 key），
            // 因为在高并发下准确移除且不引起竞态条件比较复杂。
            // 对于 L2Cache 这种 key 数量可能很大的场景，长期运行可能会有少量内存占用。
            // 如果 Key 数量非常大，建议引入 LRU 清理机制或定时清理。
            // 考虑到当前是基础实现，暂不进行复杂清理。
        }
    }

    private class Releaser : IDisposable
    {
        private readonly TKey _key;
        private readonly AsyncKeyedLocker<TKey> _locker;
        private bool _disposed;

        public Releaser(TKey key, AsyncKeyedLocker<TKey> locker)
        {
            _key = key;
            _locker = locker;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _locker.Release(_key);
        }
    }
}
