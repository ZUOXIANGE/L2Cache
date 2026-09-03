using System.Text.Json.Serialization;
using L2Cache.Abstractions.Invalidation;

namespace L2Cache.Invalidation;

/// <summary>
/// 失效消息的 JSON 源生成上下文。
/// <para>
/// 固定类型使用 source-gen 后，序列化/反序列化不再经过反射元数据构建，
/// 降低每次发布的 CPU 与分配开销，并支持 Native AOT。
/// </para>
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(InvalidationMessage))]
internal sealed partial class InvalidationMessageJsonContext : JsonSerializerContext;
