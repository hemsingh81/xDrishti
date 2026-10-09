# syntax=docker/dockerfile:1
# One build stage for the whole solution, one small runtime image per host (target: api | worker).
# Runtime images are Ubuntu "chiseled": no shell, no package manager, non-root user — minimal attack surface.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400-noble
ARG ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.12-noble-chiseled

FROM ${SDK_IMAGE} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# 1) Restore first, from project files only, so the layer is cached until a dependency changes.
#    Add a COPY line here for every new src project.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/XDrishti.Domain/XDrishti.Domain.csproj src/XDrishti.Domain/
COPY src/XDrishti.Application/XDrishti.Application.csproj src/XDrishti.Application/
COPY src/XDrishti.Infrastructure/XDrishti.Infrastructure.csproj src/XDrishti.Infrastructure/
COPY src/XDrishti.Hosting/XDrishti.Hosting.csproj src/XDrishti.Hosting/
COPY src/XDrishti.Api/XDrishti.Api.csproj src/XDrishti.Api/
COPY src/XDrishti.Worker/XDrishti.Worker.csproj src/XDrishti.Worker/
RUN --mount=type=cache,id=xd-nuget,target=/root/.nuget/packages \
    dotnet restore src/XDrishti.Api/XDrishti.Api.csproj && \
    dotnet restore src/XDrishti.Worker/XDrishti.Worker.csproj

# 2) Build and publish both hosts.
COPY src/ src/
ARG VERSION=0.1.0
RUN --mount=type=cache,id=xd-nuget,target=/root/.nuget/packages \
    dotnet publish src/XDrishti.Api/XDrishti.Api.csproj -c Release -o /out/api --no-restore -p:UseAppHost=false -p:Version=${VERSION} && \
    dotnet publish src/XDrishti.Worker/XDrishti.Worker.csproj -c Release -o /out/worker --no-restore -p:UseAppHost=false -p:Version=${VERSION}

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
