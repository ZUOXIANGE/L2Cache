namespace L2Cache.Internal;

/// <summary>
/// 异步分段锁。
/// <para>
/// 固定数量的 <see cref="SemaphoreSlim"/> 按 Key 哈希分段获取，内存占用 O(分段数)，
/// 避免按 Key 建锁在高基数场景下的无限增长。不同 Key 偶发映射到同一分段只会
/// 串行化等待，不影响正确性。
/// </para>
/// </summary>
internal sealed class AsyncKeyedLocker<TKey> where TKey : notnull
{
    private readonly SemaphoreSlim[] _semaphores;

    /// <summary>默认分段数：兼顾内存占用与碰撞概率（约 1/1024 的无关 Key 串行化概率）。</summary>
    public const int DefaultStripeCount = 1024;

    public AsyncKeyedLocker(int stripeCount = DefaultStripeCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stripeCount, 1);
        _semaphores = new SemaphoreSlim[stripeCount];
        for (var i = 0; i < stripeCount; i++)
        {
            _semaphores[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// 获取并进入锁
    /// </summary>
    /// <param name="key">锁的 Key（决定使用的分段）</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>释放锁的 Disposable 对象</returns>
    public async Task<IDisposable> LockAsync(TKey key, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores[(uint)key.GetHashCode() % (uint)_semaphores.Length];

        var entered = await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            throw new TimeoutException($"Failed to acquire memory lock for key: {key}");
        }

        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            semaphore.Release();
        }
    }
}
