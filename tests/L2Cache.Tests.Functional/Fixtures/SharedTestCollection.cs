using Xunit;

namespace L2Cache.Tests.Functional.Fixtures;

/// <summary>
/// 共享测试集合定义
/// 用于在多个测试类之间共享 RedisTestFixture
/// </summary>
[CollectionDefinition("Shared Test Collection")]
public class SharedTestCollection : ICollectionFixture<RedisTestFixture>
{
    // 这个类没有代码，也不会被实例化。
    // 它的目的仅仅是作为一个位置来应用 [CollectionDefinition] 和 ICollectionFixture<> 接口。
}
