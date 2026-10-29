# syntax=docker/dockerfile:1
# One build stage for the whole solution, one small runtime image per host (target: api | worker | feed | mcp | exec).
# Runtime images are Ubuntu "chiseled": no shell, no package manager, non-root user — minimal attack surface.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400-noble
# 10.0.13: SignalR (the hub of xd-api) in 10.0.12 and earlier has a known denial-of-service vulnerability (GHSA-334f-vvp4-7g6p).
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.12-noble-chiseled

FROM ${SDK_IMAGE} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# 1) Restore first, from project files only, so the layer is cached until a dependency changes.
#    Add a COPY line here for every new src project.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/XDrishti.Domain/XDrishti.Domain.csproj src/XDrishti.Domain/
COPY src/XDrishti.Application/XDrishti.Application.csproj src/XDrishti.Application/
COPY src/XDrishti.Indicators/XDrishti.Indicators.csproj src/XDrishti.Indicators/
COPY src/XDrishti.Strategies/XDrishti.Strategies.csproj src/XDrishti.Strategies/
COPY src/XDrishti.Strategies.File/XDrishti.Strategies.File.csproj src/XDrishti.Strategies.File/
COPY src/XDrishti.Simulation/XDrishti.Simulation.csproj src/XDrishti.Simulation/
COPY src/XDrishti.SimExport/XDrishti.SimExport.csproj src/XDrishti.SimExport/
COPY src/XDrishti.SimReader/XDrishti.SimReader.csproj src/XDrishti.SimReader/
COPY src/XDrishti.Learning.MlNet/XDrishti.Learning.MlNet.csproj src/XDrishti.Learning.MlNet/
COPY src/XDrishti.Infrastructure/XDrishti.Infrastructure.csproj src/XDrishti.Infrastructure/
COPY src/XDrishti.Hosting/XDrishti.Hosting.csproj src/XDrishti.Hosting/
COPY src/XDrishti.Api/XDrishti.Api.csproj src/XDrishti.Api/
COPY src/XDrishti.Worker/XDrishti.Worker.csproj src/XDrishti.Worker/
COPY src/XDrishti.Feed/XDrishti.Feed.csproj src/XDrishti.Feed/
COPY src/XDrishti.Mcp/XDrishti.Mcp.csproj src/XDrishti.Mcp/
COPY src/XDrishti.Execution/XDrishti.Execution.csproj src/XDrishti.Execution/
RUN --mount=type=cache,id=xd-nuget,target=/root/.nuget/packages \
    dotnet restore src/XDrishti.Api/XDrishti.Api.csproj && \
    dotnet restore src/XDrishti.Worker/XDrishti.Worker.csproj && \
    dotnet restore src/XDrishti.Feed/XDrishti.Feed.csproj && \
    dotnet restore src/XDrishti.Mcp/XDrishti.Mcp.csproj && \
    dotnet restore src/XDrishti.Execution/XDrishti.Execution.csproj

# 2) Build and publish the hosts.
COPY src/ src/
ARG VERSION=0.1.0
RUN --mount=type=cache,id=xd-nuget,target=/root/.nuget/packages \
    dotnet publish src/XDrishti.Api/XDrishti.Api.csproj -c Release -o /out/api --no-restore -p:UseAppHost=false -p:Version=${VERSION} && \
    dotnet publish src/XDrishti.Worker/XDrishti.Worker.csproj -c Release -o /out/worker --no-restore -p:UseAppHost=false -p:Version=${VERSION} && \
    dotnet publish src/XDrishti.Feed/XDrishti.Feed.csproj -c Release -o /out/feed --no-restore -p:UseAppHost=false -p:Version=${VERSION} && \
    dotnet publish src/XDrishti.Mcp/XDrishti.Mcp.csproj -c Release -o /out/mcp --no-restore -p:UseAppHost=false -p:Version=${VERSION} && \
    dotnet publish src/XDrishti.Execution/XDrishti.Execution.csproj -c Release -o /out/exec --no-restore -p:UseAppHost=false -p:Version=${VERSION}

FROM ${ASPNET_IMAGE} AS api
WORKDIR /app
COPY --from=build /out/api ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 CMD ["dotnet", "XDrishti.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "XDrishti.Api.dll"]

FROM ${RUNTIME_IMAGE} AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "XDrishti.Worker.dll"]

FROM ${RUNTIME_IMAGE} AS feed
WORKDIR /app
COPY --from=build /out/feed ./
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=10s --start-period=20s --retries=3 CMD ["dotnet", "XDrishti.Feed.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "XDrishti.Feed.dll"]

# The read-only tool server (P9-02, ADR 0137): ASP.NET Core like the API, so the aspnet base; port 8080 on the internal network only; the health check asks its own /health with the one host name it accepts.
FROM ${ASPNET_IMAGE} AS mcp
WORKDIR /app
COPY --from=build /out/mcp ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=10s --start-period=20s --retries=3 CMD ["dotnet", "XDrishti.Mcp.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "XDrishti.Mcp.dll"]

# The execution service (P12-01b, ADR 0156): a worker like the feed, no HTTP surface. Profile `live` in compose; the owner starts it with `xd-up.sh --exec`. Its health check reads its own heartbeat row.
FROM ${RUNTIME_IMAGE} AS exec
WORKDIR /app
COPY --from=build /out/exec ./
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=10s --start-period=20s --retries=3 CMD ["dotnet", "XDrishti.Execution.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "XDrishti.Execution.dll"]
