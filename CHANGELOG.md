# 变更日志

本文档记录了 L2Cache 项目的所有重要变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
并且本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [未发布]

### 新增
- 项目结构重构，符合开源项目标准
- 完善的文档体系（README、CONTRIBUTING、CHANGELOG）
- GitHub 模板文件（Issue 和 PR 模板）
- CI/CD 工作流配置
- 详细的 API 文档和架构说明

### 变更
- 优化项目目录结构
- 改进代码注释和文档

## [1.0.0] - 2024-01-XX

### 新增
- 🎯 核心缓存服务接口 `ICacheService<TKey, TValue>`
- ⚡ 基于 StackExchange.Redis 的高性能实现
- 🔒 强类型泛型接口，编译时类型检查
- 📦 批量操作支持（BatchGet、BatchPut、BatchEvict 等）
- 🔧 完整的依赖注入支持
- 🎨 业务逻辑与缓存操作解耦
- 📊 内置日志支持

### 核心功能
- **基础缓存操作**
  - `GetAsync` - 获取缓存
  - `GetOrLoadAsync` - 获取或加载缓存
  - `PutAsync` - 设置缓存
  - `PutIfAbsentAsync` - 仅在不存在时设置
  - `ReloadAsync` - 重新加载缓存
  - `EvictAsync` - 删除缓存
  - `ClearAsync` - 清空所有缓存
  - `ExistsAsync` - 检查缓存是否存在

- **批量操作**
  - `BatchGetAsync` - 批量获取缓存
  - `BatchGetOrLoadAsync` - 批量获取或加载
  - `BatchPutAsync` - 批量设置
  - `BatchReloadAsync` - 批量重新加载
  - `BatchEvictAsync` - 批量删除

- **配置选项**
  - 支持连接字符串配置
  - 支持配置文件配置
  - 支持 ConfigurationOptions 配置

### 示例项目
- 完整的 Web API 示例应用
- 品牌缓存服务实现示例
- Docker Compose 开发环境配置
- Redis Commander Web UI 集成

### 测试覆盖
- 单元测试覆盖核心功能
- 集成测试验证 Redis 交互
- 性能测试确保高性能
- 边界条件和异常情况测试

### 开发工具
- PowerShell 脚本快速启动/停止 Redis
- EditorConfig 代码格式配置
- 完整的项目解决方案文件

---

## 版本说明

### 版本号格式
本项目遵循 [语义化版本](https://semver.org/lang/zh-CN/) 规则：

- **主版本号**：当你做了不兼容的 API 修改
- **次版本号**：当你做了向下兼容的功能性新增
- **修订号**：当你做了向下兼容的问题修正

### 变更类型
- **新增** - 新功能
- **变更** - 对现有功能的变更
- **弃用** - 即将移除的功能
- **移除** - 已移除的功能
- **修复** - 问题修复
- **安全** - 安全相关的修复

### 发布周期
- **主版本**：根据需要发布，通常包含重大架构变更
- **次版本**：每月发布，包含新功能和改进
- **修订版本**：根据需要发布，主要用于 Bug 修复

### 支持政策
- **当前版本**：提供完整支持，包括新功能和 Bug 修复
- **前一个主版本**：提供 Bug 修复和安全更新
- **更早版本**：仅提供关键安全更新

---

## 贡献指南

如果您想为 L2Cache 项目做出贡献，请查看我们的 [贡献指南](CONTRIBUTING.md)。

## 许可证

本项目基于 [MIT 许可证](LICENSE) 开源。