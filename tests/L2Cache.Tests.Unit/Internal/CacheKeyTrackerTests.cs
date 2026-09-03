using L2Cache.Internal;

namespace L2Cache.Tests.Unit.Internal;

/// <summary>
/// 后台刷新 Key 跟踪器测试：登记 / 到期 / 解除跟踪
/// </summary>
public class CacheKeyTrackerTests
{
    [Test]
    public async Task Track_WhenDisabled_ShouldNotTrack()
    {
        var tracker = new CacheKeyTracker<int, string> { IsEnabled = false };

        tracker.Track(1, TimeSpan.FromMilliseconds(1));

        await Assert.That(tracker.GetDueKeys()).IsEmpty();
    }

    [Test]
    public async Task Track_WhenDue_ShouldReturnKey()
    {
        var tracker = new CacheKeyTracker<int, string> { IsEnabled = true };

        tracker.Track(1, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);

        var due = tracker.GetDueKeys().ToList();
        await Assert.That(due).Count().IsEqualTo(1);
        await Assert.That(due[0]).IsEqualTo(1);
    }

    [Test]
    public async Task Untrack_ShouldRemoveKey()
    {
        var tracker = new CacheKeyTracker<int, string> { IsEnabled = true };

        tracker.Track(1, TimeSpan.FromMilliseconds(1));
        tracker.Untrack(1);
        await Task.Delay(50);

        await Assert.That(tracker.GetDueKeys()).IsEmpty();
    }

    [Test]
    public async Task UpdateNextRefresh_ShouldPostponeDueTime()
    {
        var tracker = new CacheKeyTracker<int, string> { IsEnabled = true };

        tracker.Track(1, TimeSpan.FromMilliseconds(1));
        await Task.Delay(50);
        tracker.UpdateNextRefresh(1);

        await Assert.That(tracker.GetDueKeys()).IsEmpty();
    }

    [Test]
    public async Task Track_SameKeyTwice_ShouldReplaceInterval()
    {
        var tracker = new CacheKeyTracker<int, string> { IsEnabled = true };

        tracker.Track(1, TimeSpan.FromMilliseconds(1));
        tracker.Track(1, TimeSpan.FromSeconds(60));
        await Task.Delay(50);

        await Assert.That(tracker.GetDueKeys()).IsEmpty();
    }
}
