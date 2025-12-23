# 贡献指南

感谢您对 L2Cache 项目的关注！我们欢迎所有形式的贡献，包括但不限于：

- 🐛 报告 Bug
- 💡 提出新功能建议
- 📝 改进文档
- 🔧 提交代码修复或新功能
- 🧪 编写测试用例
- 📖 翻译文档

## 🚀 快速开始

### 开发环境准备

1. **安装必要工具**
   - [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本
   - [Docker](https://www.docker.com/get-started) (用于运行 Redis)
   - [Git](https://git-scm.com/)
   - 推荐使用 [Visual Studio](https://visualstudio.microsoft.com/) 或 [Visual Studio Code](https://code.visualstudio.com/)

2. **克隆项目**
   ```bash
   git clone https://github.com/ZUOXIANGE/L2Cache.git
   cd L2Cache
   ```

3. **启动开发环境**
   ```bash
   # 一键启动本地依赖环境（推荐）
   ./scripts/dev-up.ps1 [-Monitoring] [-Benchmarks]

   # 恢复与构建
   dotnet restore
   dotnet build

   # 运行测试
   dotnet test

   # 开发结束后可停止环境
   ./scripts/dev-down.ps1

   # 轻量模式（仅 Redis，如需最简）
   ./scripts/start-redis.ps1
   ./scripts/stop-redis.ps1
   ```

## 📋 贡献流程

### 1. 创建 Issue

在开始编码之前，请先创建一个 Issue 来描述您要解决的问题或添加的功能：

- **Bug 报告**：请使用 Bug 报告模板，详细描述问题的重现步骤
- **功能请求**：请使用功能请求模板，说明新功能的用途和预期行为
- **文档改进**：描述需要改进的文档部分

### 2. Fork 和分支

1. Fork 本项目到您的 GitHub 账户
2. 克隆您的 Fork 到本地
3. 创建新的功能分支：
   ```bash
   git checkout -b feature/your-feature-name
   # 或者对于 bug 修复
   git checkout -b fix/issue-number-description
   ```

### 3. 开发规范

#### 代码风格

- 遵循 [C# 编码约定](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions)
- 使用项目根目录的 `.editorconfig` 文件配置
- 确保代码通过所有静态分析检查

#### 命名约定

- **类名**：使用 PascalCase，如 `CacheService`
- **方法名**：使用 PascalCase，如 `GetAsync`
- **变量名**：使用 camelCase，如 `cacheKey`
- **常量**：使用 UPPER_CASE，如 `CACHE_NAME`
- **接口**：以 `I` 开头，如 `ICacheService`

#### 注释和文档

- 所有公共 API 必须有 XML 文档注释
- 复杂的业务逻辑需要添加注释说明
- 示例：
  ```csharp
  /// <summary>
  /// 获取或加载缓存数据
  /// </summary>
  /// <param name="key">缓存键</param>
  /// <param name="expiry">过期时间</param>
  /// <returns>缓存数据</returns>
  Task<TValue> GetOrLoadAsync(TKey key, TimeSpan? expiry = null);
  ```

#### 测试要求

- 新功能必须包含单元测试
- 测试覆盖率应保持在 80% 以上
- 测试方法命名格式：`MethodName_Scenario_ExpectedResult`
- 示例：
  ```csharp
  [Test]
  public async Task GetOrLoadAsync_WhenCacheExists_ShouldReturnCachedValue()
  {
      // Arrange
      // Act
      // Assert
  }
  ```

### 4. 提交规范

#### Commit 消息格式

使用 [Conventional Commits](https://www.conventionalcommits.org/) 格式：

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**类型 (type)：**
- `feat`: 新功能
- `fix`: Bug 修复
- `docs`: 文档更新
- `style`: 代码格式调整（不影响功能）
- `refactor`: 代码重构
- `test`: 测试相关
- `chore`: 构建过程或辅助工具的变动

**示例：**
```bash
feat(core): add batch cache operations support

- Add BatchGetAsync method for bulk cache retrieval
- Add BatchPutAsync method for bulk cache storage
- Update ICacheService interface with new methods

Closes #123
```

### 5. Pull Request

1. **确保代码质量**
   ```bash
   # 运行所有测试
   dotnet test
   
   # 检查代码格式
   dotnet format --verify-no-changes
   
   # 构建项目
   dotnet build --configuration Release
   ```

2. **创建 Pull Request**
   - 使用清晰的标题描述您的更改
   - 在描述中引用相关的 Issue
   - 填写 PR 模板中的所有必要信息
   - 添加适当的标签

3. **代码审查**
   - 响应审查者的反馈
   - 根据建议进行必要的修改
   - 保持 PR 的更新和同步

## 🧪 测试指南

### 运行测试

```bash
# 运行所有测试
dotnet test

# 运行特定项目的测试
dotnet test tests/L2Cache.Examples.Tests/

# 生成测试覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

### 测试分类

- **单元测试**：测试单个方法或类的功能
- **集成测试**：测试组件之间的交互
- **性能测试**：测试缓存操作的性能

### 测试数据

- 使用 `TestHelpers` 类提供的测试数据
- 避免硬编码测试数据
- 确保测试之间相互独立

## 📚 文档贡献

### 文档类型

- **API 文档**：位于 `docs/api/` 目录
- **用户指南**：位于 `docs/guides/` 目录
- **示例代码**：位于 `docs/examples/` 目录

### 文档规范

- 使用 Markdown 格式
- 包含代码示例
- 提供清晰的步骤说明
- 保持文档与代码同步

## 🐛 Bug 报告

### 报告 Bug 前

1. 搜索现有的 Issues，确认问题未被报告
2. 尝试在最新版本中重现问题
3. 收集相关的错误信息和日志

### Bug 报告模板

请包含以下信息：

- **环境信息**：操作系统、.NET 版本、L2Cache 版本
- **重现步骤**：详细的步骤说明
- **预期行为**：您期望发生什么
- **实际行为**：实际发生了什么
- **错误信息**：完整的错误消息和堆栈跟踪
- **附加信息**：相关的配置文件、日志等

## 💡 功能请求

### 提交功能请求前

1. 检查是否已有类似的功能请求
2. 考虑功能的通用性和必要性
3. 思考功能的实现方式

### 功能请求模板

请包含以下信息：

- **功能描述**：清晰地描述新功能
- **使用场景**：说明功能的使用场景
- **预期行为**：详细描述功能的预期行为
- **替代方案**：是否考虑过其他解决方案
- **附加信息**：相关的参考资料或示例

## 🏷️ 发布流程

### 版本号规则

遵循 [语义化版本](https://semver.org/) 规则：

- **主版本号**：不兼容的 API 修改
- **次版本号**：向下兼容的功能性新增
- **修订号**：向下兼容的问题修正

### 发布检查清单

- [ ] 所有测试通过
- [ ] 文档已更新
- [ ] CHANGELOG.md 已更新
- [ ] 版本号已更新
- [ ] NuGet 包配置正确

## 📞 获取帮助

如果您在贡献过程中遇到问题，可以通过以下方式获取帮助：

- 💬 [GitHub Discussions](https://github.com/ZUOXIANGE/L2Cache/discussions)
- 📧 发送邮件至：contributors@l2cache.org
- 📋 创建 Issue 并添加 `question` 标签

## 🙏 致谢

感谢所有为 L2Cache 项目做出贡献的开发者！您的贡献让这个项目变得更好。

---

再次感谢您的贡献！🎉