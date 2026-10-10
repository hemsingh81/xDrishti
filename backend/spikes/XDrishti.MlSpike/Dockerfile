# syntax=docker/dockerfile:1
# P0-16 spike images (build context = backend/). Targets show each step of getting LightGBM running in the shipped runtime image:
#   plain     chiseled runtime + app only (what NuGet gives us: no linux-arm64 LightGBM library)
#   lib-only  + lib_lightgbm.so built from source (still lacks OpenMP)
#   runtime   + libgomp.so.1 copied from the build stage  → the working recipe for the worker image
#   sdk       the SDK image with the app and the library (reference environment)
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0.400-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0.12-noble-chiseled

FROM ${SDK_IMAGE} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY spikes/XDrishti.MlSpike/XDrishti.MlSpike.csproj spikes/XDrishti.MlSpike/
RUN dotnet restore spikes/XDrishti.MlSpike/XDrishti.MlSpike.csproj -r linux-arm64
COPY spikes/XDrishti.MlSpike/ spikes/XDrishti.MlSpike/
RUN dotnet publish spikes/XDrishti.MlSpike/XDrishti.MlSpike.csproj -c Release -r linux-arm64 --self-contained false --no-restore -p:UseAppHost=false -o /out

# LightGBM native library for linux-arm64, built from the pinned release tag (commit verified) — the NuGet package ships x64 only.
FROM ${SDK_IMAGE} AS lightgbm
ARG LIGHTGBM_TAG=v4.6.0
ARG LIGHTGBM_COMMIT=d02a01ac6f51d36c9e62388243bcb75c3b1b1774
ARG LIGHTGBM_CXX_FLAGS=""
ARG LIGHTGBM_CMAKE_ARGS=""
RUN apt-get update && apt-get install -y --no-install-recommends git cmake g++ make ca-certificates && rm -rf /var/lib/apt/lists/*
RUN git clone --recursive --depth 1 --branch ${LIGHTGBM_TAG} https://github.com/microsoft/LightGBM /lightgbm \
 && test "$(git -C /lightgbm rev-parse HEAD)" = "${LIGHTGBM_COMMIT}" \
 && cmake -S /lightgbm -B /lightgbm/build -DCMAKE_BUILD_TYPE=Release -DUSE_OPENMP=ON "-DCMAKE_CXX_FLAGS=${LIGHTGBM_CXX_FLAGS}" ${LIGHTGBM_CMAKE_ARGS} \
 && cmake --build /lightgbm/build --target _lightgbm -j"$(nproc)" \
 && cp /lightgbm/lib_lightgbm.so /lib_lightgbm.so \
 && cp -L /usr/lib/aarch64-linux-gnu/libgomp.so.1 /libgomp.so.1

FROM ${RUNTIME_IMAGE} AS plain
WORKDIR /app
COPY --from=build /out /app
USER $APP_UID
ENTRYPOINT ["dotnet", "/app/xd-mlspike.dll"]

FROM ${RUNTIME_IMAGE} AS lib-only
WORKDIR /app
COPY --from=build /out /app
COPY --from=lightgbm /lib_lightgbm.so /app/lib_lightgbm.so
USER $APP_UID
ENTRYPOINT ["dotnet", "/app/xd-mlspike.dll"]

FROM ${RUNTIME_IMAGE} AS runtime
WORKDIR /app
COPY --from=build /out /app
COPY --from=lightgbm /lib_lightgbm.so /app/lib_lightgbm.so
COPY --from=lightgbm /libgomp.so.1 /usr/lib/aarch64-linux-gnu/libgomp.so.1
USER $APP_UID
ENTRYPOINT ["dotnet", "/app/xd-mlspike.dll"]

FROM ${SDK_IMAGE} AS sdk
WORKDIR /app
COPY --from=build /out /app
COPY --from=lightgbm /lib_lightgbm.so /app/lib_lightgbm.so
ENTRYPOINT ["dotnet", "/app/xd-mlspike.dll"]

# Reference only: the same app on linux/amd64 (emulated) with the official x64 LightGBM library from NuGet — tells apart
# "our arm64 library build is wrong" from "ML.NET + LightGBM misbehave everywhere". Published natively, only *run* under emulation.
FROM build AS build-x64
RUN dotnet publish spikes/XDrishti.MlSpike/XDrishti.MlSpike.csproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o /out-x64

FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/runtime:10.0.12-noble AS x64-sdk
WORKDIR /app
COPY --from=build-x64 /out-x64 /app
RUN apt-get update && apt-get install -y --no-install-recommends libgomp1 && rm -rf /var/lib/apt/lists/*
ENTRYPOINT ["dotnet", "/app/xd-mlspike.dll"]
