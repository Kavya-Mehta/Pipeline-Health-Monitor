# ── Stage 1: Build ───────────────────────────────────────────────────────────
# The SDK image contains the full .NET compiler and toolchain.
# It's large (~800MB) but we only use it to compile — it never goes to production.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the .csproj and restore packages FIRST, before copying any source code.
# Docker builds images in layers. If PipelineHealthMonitor.csproj hasn't changed,
# Docker reuses the cached restore layer — packages are NOT re-downloaded on every build.
# This one trick makes rebuilds after code changes ~10x faster.
COPY Pipeline-Health-Monitor/PipelineHealthMonitor.csproj Pipeline-Health-Monitor/
RUN dotnet restore Pipeline-Health-Monitor/PipelineHealthMonitor.csproj

# Now copy the actual source code and publish a Release build.
# /p:UseAppHost=false means "don't generate a platform-specific exe wrapper" —
# we run via `dotnet PipelineHealthMonitor.dll` instead.
COPY Pipeline-Health-Monitor/ Pipeline-Health-Monitor/
RUN dotnet publish Pipeline-Health-Monitor/PipelineHealthMonitor.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
# The aspnet runtime image is much smaller (~220MB) — no compiler, no SDK.
# Only the compiled output from Stage 1 is copied in.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PipelineHealthMonitor.dll"]
