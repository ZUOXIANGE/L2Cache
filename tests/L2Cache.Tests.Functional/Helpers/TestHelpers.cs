namespace L2Cache.Tests.Functional.Helpers;

/// <summary>
/// 测试辅助类
/// 提供生成测试数据等通用功能
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// 获取随机字符串
    /// </summary>
    /// <param name="length">长度，默认为 10</param>
    /// <returns>随机字符串</returns>
    public static string GetRandomString(int length = 10)
    {
        return Guid.NewGuid().ToString("N").Substring(0, Math.Min(length, 32));
    }

    /// <summary>
    /// 生成测试用的字典数据
    /// </summary>
    /// <param name="count">数据条数</param>
    /// <returns>键值对字典</returns>
    public static Dictionary<string, string> GenerateTestData(int count)
    {
        var data = new Dictionary<string, string>();
        for (int i = 0; i < count; i++)
        {
            data[$"key-{i}"] = $"value-{i}-{Guid.NewGuid()}";
        }
        return data;
    }
}
