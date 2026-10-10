#!/usr/bin/env bash
# Samples memory every N seconds while a benchmark runs (P0-15): VM free/used, host swap, host free pages. Ctrl-C or kill to stop.
#   scripts/llm-mem.sh data/llm-bench/mem-main-container.csv [interval-seconds]
set -euo pipefail
out="${1:?output csv}"; every="${2:-5}"
echo "time,vm_used_mb,vm_free_mb,vm_available_mb,host_swap_used_mb,host_free_pages,host_pressure_free_pct" >"$out"
while true; do
  vm=$(podman machine ssh 'free -m | awk "/Mem:/{print \$3\",\"\$4\",\"\$7}"' 2>/dev/null || echo ",,")
  swap=$(sysctl -n vm.swapusage | awk '{gsub("M","",$6); print $6}')
  free=$(vm_stat | awk '/Pages free/{gsub("\\.","",$3); print $3}')
  press=$(memory_pressure 2>/dev/null | awk '/free percentage/{gsub("%","",$5); print $5}')
  echo "$(date +%H:%M:%S),$vm,$swap,$free,$press" >>"$out"
  sleep "$every"
done
