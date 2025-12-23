# L2Cache基准测试Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制解决方案文件
COPY ["L2Cache.slnx", "./"]
COPY ["src/L2Cache/L2Cache.csproj", "src/L2Cache/"]
COPY ["benchmarks/L2Cache.Benchmarks/L2Cache.Benchmarks.csproj", "benchmarks/L2Cache.Benchmarks/"]

# 还原依赖
RUN dotnet restore "benchmarks/L2Cache.Benchmarks/L2Cache.Benchmarks.csproj"

# 复制源代码
COPY ["src/", "src/"]
COPY ["benchmarks/", "benchmarks/"]

# 构建应用
WORKDIR "/src/benchmarks/L2Cache.Benchmarks"
RUN dotnet build "L2Cache.Benchmarks.csproj" -c Release -o /app/build

# 发布应用
FROM build AS publish
RUN dotnet publish "L2Cache.Benchmarks.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 运行时镜像
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

# 创建非root用户
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser:appuser /app
USER appuser

# 复制发布文件
COPY --from=publish /app/publish .

# 设置环境变量
ENV DOTNET_ENVIRONMENT=Production
ENV REDIS_CONNECTION_STRING=redis:6379

# 启动基准测试
ENTRYPOINT ["dotnet", "L2Cache.Benchmarks.dll"]