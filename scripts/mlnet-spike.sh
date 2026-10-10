#!/usr/bin/env bash
# ML.NET gradient-boosted trees spike (P0-16). Runs the same code in different environments; results go to data/mlnet-spike/.
#   scripts/mlnet-spike.sh host    probe|run [options]       host-native (macOS arm64)
#   scripts/mlnet-spike.sh plain|lib-only|runtime|sdk  probe|run [options]   linux/arm64 container, 8 CPUs / 8 GB, read-only root, non-root (sdk: root)
# Options for `run`: --trainer lightgbm|fasttree|both  --rows N  --threads N  --trees N  --leaves N  --repeats N  (--env is set for you)
# Images are built from backend/spikes/XDrishti.MlSpike/Dockerfile (build stage needs network for NuGet, apt and the pinned LightGBM source).
set -euo pipefail
source "$(dirname "$0")/_env.sh"
target="${1:?host|plain|lib-only|runtime|sdk}"; shift
out="$ROOT/data/mlnet-spike"; mkdir -p "$out"
if [[ "$target" == "host" ]]; then
  cd "$ROOT/backend"
  exec dotnet run --project spikes/XDrishti.MlSpike -c Release -- "$1" --env host-macos-arm64 --out "$out" "${@:2}"
fi
image="xd-mlspike:$target"
if ! podman image exists "$image" || [[ -n "${REBUILD:-}" ]]; then
  podman build ${REBUILD_PLATFORM:+--platform "$REBUILD_PLATFORM"} -f "$ROOT/backend/spikes/XDrishti.MlSpike/Dockerfile" --target "$target" --build-arg "LIGHTGBM_CXX_FLAGS=${LIGHTGBM_CXX_FLAGS:-}" --build-arg "LIGHTGBM_CMAKE_ARGS=${LIGHTGBM_CMAKE_ARGS:-}" -t "$image" "$ROOT/backend"
fi
user=(); [[ "$target" != "sdk" && "$target" != "x64-sdk" ]] && user=(--user 1654)
platform=(); [[ "$target" == "x64-sdk" ]] && platform=(--platform linux/amd64)
exec podman run --rm ${platform[@]+"${platform[@]}"} --cpus 8 --memory 8g --read-only --tmpfs /tmp ${user[@]+"${user[@]}"} -v "$out:/out" "$image" "$1" --env "container-$target" --out /out "${@:2}"
