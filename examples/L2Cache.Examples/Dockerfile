# L2Cache Examples Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and projects
COPY ["L2Cache.slnx", "./"]
COPY ["src/L2Cache/L2Cache.csproj", "src/L2Cache/"]
COPY ["examples/L2Cache.Examples/L2Cache.Examples.csproj", "examples/L2Cache.Examples/"]

# Restore dependencies
RUN dotnet restore "examples/L2Cache.Examples/L2Cache.Examples.csproj"

# Copy source
COPY ["src/", "src/"]
COPY ["examples/", "examples/"]

# Build
WORKDIR "/src/examples/L2Cache.Examples"
RUN dotnet build "L2Cache.Examples.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "L2Cache.Examples.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

# Install curl for health check
# RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser:appuser /app
USER appuser

# Copy published files
COPY --from=publish /app/publish .

# Health check
# HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
#   CMD curl -f http://localhost:5000/health || exit 1

# Start app
ENTRYPOINT ["dotnet", "L2Cache.Examples.dll"]
