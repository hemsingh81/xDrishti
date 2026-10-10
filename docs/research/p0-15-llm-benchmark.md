# P0-15 — xd-llm GPU benchmark under libkrun

- **Spec:** [P0-15](../specs/P0-15-xd-llm-gpu-benchmark-under-libkrun.md) (Done; owner accepted Q1–Q6 defaults on 2026-10-09)
- **Status:** **done 2026-10-10** — all runs complete, decision recorded ([ADR 0005](../adr/0005-local-llm-runtime-and-models.md)); raw reports in [llm-bench-results/](llm-bench-results/)
- **Harness:** `backend/spikes/XDrishti.LlmBench` (+ Tests) · `scripts/llm-bench.sh` · results in `data/llm-bench/` (git-ignored)

## Environment (recorded 2026-10-09, before any change)
| Item | Value |
|---|---|
| Host | Mac16,7 (Apple M4 Pro), 48 GB unified memory, macOS 26.7, ~200 GB free disk |
| Podman | 6.0.2, machine `podman-machine-default`, provider libkrun, **10 CPUs, 24 GiB RAM, 120 GiB disk** (24 GiB used) |
| GPU in the VM | `/dev/dri/renderD128` present; VM kernel 7.1.3 (Fedora aarch64) |
| Installers | `uv` available; no Homebrew; system Python 3.9.6; no `ramalama`, no `llama-server` |
| Original VM settings to restore if needed | CPUs 10 · Memory 24576 MiB · Disk 120 GiB |

## Pinned artifacts (all downloaded and verified on 2026-10-09/10)
Models come from public, ungated Hugging Face repositories, pinned to the repository commit shown; each file's SHA-256 (from the hub's metadata) was verified after download by `scripts/llm-models.sh`.

| Role | Repository @ commit | File | Size (bytes) | SHA-256 | Licence |
|---|---|---|---|---|---|
| Main | `Qwen/Qwen3-30B-A3B-GGUF` @ `e4d4bafdfb96` | `Qwen3-30B-A3B-Q4_K_M.gguf` | 18,556,685,824 (18.6 GB) | `0d003f6662faee786ed5da3e31b29c978de5ae5d275c8794c606a7f3c01aa8f5` | Apache-2.0 |
| Main (alt) | `Qwen/Qwen3-14B-GGUF` @ `530227a7d994` | `Qwen3-14B-Q4_K_M.gguf` | 9,001,752,960 (9.0 GB) | `500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0` | Apache-2.0 |
| Main (alt) | `ggml-org/gpt-oss-20b-GGUF` @ `ef9b12f2ff56` | `gpt-oss-20b-MXFP4.gguf` | 12,109,566,624 (12.1 GB) | `27cd6c432c7672cb812a92f611cf3ba7bbc35928262bb1e1253ff4ee6ae35901` | Apache-2.0 |
| Small | `Qwen/Qwen3-8B-GGUF` @ `7c41481f57cb` | `Qwen3-8B-Q4_K_M.gguf` | 5,027,783,488 (5.0 GB) | `d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785` | Apache-2.0 |
| Embeddings | `nomic-ai/nomic-embed-text-v1.5-GGUF` @ `0188c9bf4097` | `nomic-embed-text-v1.5.Q8_0.gguf` | 146,146,432 (0.15 GB) | `3e24342164b3d94991ba9692fdc0dd08e3fd7362e0aacc396a9a5c54a544c3b7` | Apache-2.0 |
| | | | **≈ 44.8 GB total** | | |

Tools:
| Tool | Source | Size | Pin |
|---|---|---|---|
| RamaLama (container path) | PyPI `ramalama==0.25.0`, installed with `uv tool install` (user-local, no sudo) | wheel 241,851 bytes | sha256 `889066febc228f4f149b08d5a06c9d0ff2f7f0fa001b2c8a28b4629dbfc43c7d` |
| RamaLama container image | `quay.io/ramalama/ramalama:0.25.0`, run by digest (`scripts/llm-setup.sh` pulls it) | 1.02 GB on disk | manifest digest `sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e` |
| llama.cpp native (Metal) | GitHub release `b11530` (a pre-release build, 2026-10-09): `llama-b11530-bin-macos-arm64.tar.gz` — installed and verified by `scripts/llm-setup.sh` | 12,084,306 bytes | sha256 `547b15ee63b09438d2fcc75acd8d5b10336f7f16bf686c774ccd24c91c6f015b` |


Storage: models in `~/Library/Application Support/xDrishti/models` (outside the repo, shared read-only into the container); native llama.cpp in
`~/Library/Application Support/xDrishti/llm/llama.cpp-b11530`. Total disk ≈ 45 GB models + image; the chosen model is copied into a VM volume only if the container path wins.

Not chosen, on purpose: a web source mentions newer Qwen releases (3.5/3.6) but none appear in the official Qwen GGUF listing, so they are left out; GLM-4.7-Flash (30B, ~3B active) was mentioned for tool use but with reported agent-loop problems — can be added as a fourth candidate if you want it.

## Procedure (as run, 2026-10-09/10)
1. `scripts/llm-setup.sh` (llama.cpp + image), `scripts/llm-models.sh` (models). Servers by `scripts/llm-serve.sh native|cpu|container <model> 32768` (all bound to 127.0.0.1; the embedding model on 8084).
2. One benchmark per model × runtime with `scripts/llm-bench.sh`, then the memory, interference and load-time studies below. Memory sampled every 5 s by `scripts/llm-mem.sh`.
3. Decision against the spec thresholds → [ADR 0005](../adr/0005-local-llm-runtime-and-models.md).

### Commands used (reports in [llm-bench-results/](llm-bench-results/))
| Report(s) | Server | Bench flags |
|---|---|---|
| `small_*`, `main30b_*`, `main30b_*_volume-repeat` (Qwen, thinking disabled) | `container`/`native` `<small\|main> 32768` | `--ctx 1024,8192,30000 --repeats 3 --gen 256 --no-think` (+ `--smoke`, `--embeddings-url`, `--load-ms N` where shown) |
| `dense14b_*` | same, model `dense` | `--ctx 1024,8192 --repeats 3 --gen 256 --no-think --smoke` |
| `gptoss20b_*` (reasoning model, **thinking on**) | same, model `gptoss` | `--ctx 1024,8192 --repeats 3 --gen 256 --smoke` (no `--no-think`) |
| `small_cpu-baseline` | `cpu small 8192` (host llama.cpp, `-ngl 0`) | `--ctx 1024 --repeats 2 --gen 64 --no-think` |
| `main30b_container-gpu_cpuload` | as the 30B container run | `--ctx 8192 --repeats 3 --gen 256 --no-think`, with 8 `yes > /dev/null` processes running **on the host** |
| `*_smoke-rerun*` | as above | `--ctx 1024 --repeats 1 --gen 16 --smoke` (after the harness fixes below) |
`--load-ms` was typed in from the measured start-to-healthy time of that server instance (first load after the previous server was stopped). The briefing test generates at most 500 tokens.

## Results (median of 3 runs; prompts sized with the server's own tokenizer; `cache_prompt` off)
Generation speed in tokens/s at ~1K / ~8K / ~30K prompt tokens; prompt-processing speed in tokens/s; the briefing test is a ~3.8K-token prompt answered in about 300 words (cap 500 tokens).

| Model | Runtime | Generation tok/s | Prompt tok/s | Briefing: first token / total | Load (first load of that server) | Smoke |
|---|---|---|---|---|---|---|
| Qwen3-8B (dense) | CPU only (baseline) | 24 @1K | 112 @1K | 41 s / 72 s | 6.4 s | — |
| Qwen3-8B | container GPU | 34.8 / 29.1 / 19.6 | 251 / 232 / 128 | 13.2 s / 28.8 s | 10.1 s | all pass |
| Qwen3-8B | native Metal | 42.0 / 34.9 / 22.4 | 389 / 356 / 196 | 10.5 s / 23.6 s | 6.4 s | all pass |
| Qwen3-14B (dense) | container GPU | 20.4 / 17.1 / – | 165 / 134 / – | **27.5 s / 53.8 s** | 44.3 s | all pass |
| Qwen3-14B | native Metal | 26.0 / 21.9 / – | 225 / 199 / – | **19.1 s / 41.2 s** | 4.7 s | all pass |
| **Qwen3-30B-A3B (MoE)** | container GPU | **52.0 / 40.9 / 24.0** | 369 / 312 / 133 | 10.3 s / 20.0 s | 61 s (first, downloads running) · see load-time table | all pass |
| Qwen3-30B-A3B | native Metal | 73.5 / 54.3 / 27.5 | 631 / 536 / 210 | 6.8 s / 15.9 s | 9.5 s | all pass |
| **gpt-oss-20b (MoE, thinking on)** | container GPU | 63.9 / 56.8 / – | 403 / 462 / – | 8.1 s† / 16.2 s† | 25–68 s (67.5 first, 40.3, 25.5) | all pass (after harness fixes) |
| gpt-oss-20b | native Metal | 72.3 / 66.9 / – | 715 / 793 / – | 4.6 s† / 11.7 s† | 6.2 s | all pass (after harness fixes) |
| nomic-embed-text v1.5 (Q8) | CPU | – | – | – | – | 768-dimension vectors |

Smoke = tool calls valid 10/10, JSON mode, needle found in a ~30K-token **answer**. Two harness limits were found by the review and fixed: the JSON and needle checks gave 100 / 64 tokens, which gpt-oss (a reasoning model) spends on reasoning, and the needle test also counted words quoted in reasoning. Re-runs: native `gptoss20b_native-metal_smoke-rerun.md` (JSON fixed; before the needle fix, not re-run with the final harness), container `gptoss20b_container-gpu_smoke-rerun.md` (JSON fixed, **needle failed** — 64-token budget spent on reasoning) and `gptoss20b_container-gpu_smoke-rerun2.md` (final harness: tool calls 10/10, JSON pass, needle pass, load 25.5 s). Qwen and 14B smoke results used the first harness; thinking was off there and the later fixes only made the checks stricter.
† **gpt-oss briefing numbers are not comparable to Qwen's.** Qwen was run with thinking off and wrote 464–500 tokens of briefing; gpt-oss spent all 500 tokens on reasoning ("We need to write a daily briefing…") and never reached the answer, so its end-to-end time is *understated* (a real briefing needs the reasoning plus ~400 answer tokens). Compare only the generation and prompt speeds: gpt-oss generates **1.23× (1K), 1.39× (8K)** as fast as Qwen3-30B-A3B in the container. Tool-call and JSON results for Qwen were also measured with thinking off; P9 may run with it on — the P9-07 eval must test the configuration actually used.
A 30K-token prompt takes 2.5–4 minutes to read on the 8B and 30B models.

### Container vs native (measured ranges)
Generation: container = **71–88%** of native (native is 1.13–1.41× faster; 30B @1K 71%, gpt-oss @1K 88%). Prompt reading: container = **56–73%** of native (native 1.4–1.8× faster).

### Variance and confounding
- Within one run the 3 repeats differ by 0–3% in most reports. Larger spreads: native 8B briefing 16%, native 30B briefing 11%, native 14B at 8K 11%, native 30B at 30K 7%, native 14B briefing 7%, 30B container at 1K ~5%. These runs happened while models were downloading (network; disk and CPU not idle) — **a hypothesis for the spread, not demonstrated**. The gpt-oss runs, the interference run and the re-runs started after the last download finished.
- Session to session the same configuration moved by 4–8%: 30B container generation 52.0 → 56.1 tok/s at 1K, 40.9 → 42.5 at 8K, briefing 47.3 → 50.0 (`main30b_container-gpu_volume-repeat.md`, quiet machine). Treat differences under ~10% as noise; the container-vs-native and MoE-vs-dense gaps are far larger.

### Load time depends on host memory, not on the mount (re-measured on a quiet machine)
| Measurement (30B container, 24 GB VM) | Load |
|---|---|
| First load, host folder, downloads running (benchmark report) | 61.3 s |
| Warm reload, host folder (before the CPU-load test) | 44.5 s |
| VM volume `xd-models`, first / second load / cache dropped (first re-check, console only) | 28.2 s / 22.5 s / 39.6 s |
| Quiet re-run [`load-times.md`](llm-bench-results/load-times.md): host folder cache dropped / warm | 37.0 s / 38.1 s |
| Quiet re-run: VM volume cache dropped | 29.1 s |
| Quiet re-run: VM volume warm #1 / #2 | **104.8 s / 99.5 s** |
Range **22–105 s**. **Cause not established.** The sampled 61 s load coincided with macOS swap rising from 28.7 to 38.8 GB; the 100–105 s loads (01:29–01:37) were not memory-sampled, and the same volume loaded in 29 s with the VM cache dropped, so the VM's own page cache / double buffering inside the 24 GB VM is an equally plausible cause. A memory sample next to each load would settle it (P9-01). The earlier belief that a Podman volume loads faster than the shared folder is **not supported**; the volume is an option, not a requirement.

### Answers to the spec's questions
| Question | Answer |
|---|---|
| GPU really used in the container? | **Yes.** [`gpu-offload-30b-container.txt`](llm-bench-results/gpu-offload-30b-container.txt): `using device Vulkan0 (Virtio-GPU Venus (Apple M4 Pro))`, `offloaded 49/49 layers to GPU` (30B); and, for the 8B, prompt reading is ~2.2× the CPU-only baseline (251 vs 112 tok/s). The "CPU-only baseline" is host llama.cpp with `-ngl 0` (2 repeats, 1K context only), not a CPU run inside the VM. The `Opening /dev/dri/card0 failed` log line is harmless noise. |
| Container GPU vs host-native | See the measured ranges above. Both pass the thresholds for the MoE models (except load time in pressured conditions). |
| Dense vs MoE | The MoE models are faster: Qwen3-30B-A3B generates 52 tok/s in the container vs 17–20 for the dense 14B. The dense 14B **fails the first-token threshold** (27.5 s) and is rejected. |
| Memory: VM size needed beside the stack | **No resize needed.** [`mem-main-container.csv`](llm-bench-results/mem-main-container.csv): with the 30B loaded and the full stack running, VM "used" peaked at 5.3 GB with 18.6 GB available (the VM's free column dipped to 216 MB only because page cache fills it); no OOM, no restart; `/api/system/status` reported healthy before and after (no snapshot committed). The stack is still a skeleton (db, migrate, api, worker, proxy — idle), so this says little about future load. That weights live in GPU memory on the host side is **inferred** from these numbers, not separately measured; a `mem_limit` for the service must be measured in P9-01. |
| **Host memory (threshold "no swapping on the host": NOT MET)** | The Mac was already swapping before any model loaded: container run — swap 28.7 GB → 38.8 GB one minute after the load → 40–42.7 GB; native run ≥ 38.9 GB → 42.5 GB (only these two runs were sampled); free memory 18–21%. The benchmark could not be run in a swap-free state, so the threshold cannot be satisfied on this machine as it is used today. This — not speed — is why the decision is **on-demand start** and why load time varies so much. |
| Interference with CPU-heavy jobs | 8 `yes > /dev/null` processes on the host: generation −8% (40.9 → 37.7 tok/s at 8K — **within the 4–8% session-to-session noise**), prompt reading −16% and briefing +16% (20.0 → 23.2 s) — clearly outside it. Compared across separate server sessions. Only the LLM's slowdown was measured, **not** the LLM's effect on the API/worker, which is the direction that matters for the nightly pipeline — to be checked in P9-01. Policy: one request at a time; LLM jobs outside the nightly window. |
| Tool-call validity / JSON / embeddings / 32K needle | All pass on every model after the harness fixes (tool calls 10/10). Quality beyond these smoke tests is the P9-07 eval set's job. |
| Thresholds (30B-A3B, container) | 40.9–42.5 tok/s @8K (≥ 15) pass · first token 9.7–10.3 s (≤ 15) pass · briefing 19.0–20.0 s (≤ 45) pass · tool calls 10/10 pass · **load 22–105 s (≤ 60): passes in most conditions, fails under host memory pressure** · **host swap-free: not met**. |

### Caveats for the decision
- **Why the container is still proposed although it misses the load threshold in some conditions** (the spec says native would then be the answer): native has no such problem and is faster, but the container keeps the model server inside the compose stack (same isolation, secrets handling, network policy and start/stop as every other service) and meets every speed threshold. This is a judgement call, flagged in the ADR for the owner to confirm.
- `-np 1` means a quick classification job queues behind a long briefing; the MoE is fast enough that a separate small model was judged unnecessary, but P9 should keep jobs short or use a second server if it hurts.
- The ≤ 8K-token prompt budget costs ~26 s to the first word in the container (8K row), 10 s at 4K.
- Newer open models than those tested may exist; re-run `scripts/llm-bench.sh` against any new candidate — it needs only an OpenAI-compatible endpoint.

### Incidents recorded
- **RamaLama's `serve` published the model server on all interfaces (0.0.0.0).** It ran for about two minutes with no data or secrets; I stopped it and replaced it with our own `podman run` bound to 127.0.0.1 (`scripts/llm-serve.sh`). The `ramalama` CLI was uninstalled; only its container image (digest-pinned) is used.
- The first image pull hit a TLS error and fell back to `:latest`; the pulled image's digest equals the pinned one (`podman image inspect` → `sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e`, the manifest digest quay lists for `0.25.0` and `latest`).

Cleanup done 2026-10-10: the dense 14B and the 8B model files were deleted (rejected / not needed), the `ramalama` CLI was uninstalled, no server or container is left running. Kept: Qwen3-30B-A3B, gpt-oss-20b, the embedding model (≈ 30.8 GB on the host), the Podman volume `xd-models` (the 30B copy, 18.6 GB) and the 1 GB image — ≈ 51 GB in total, within the 60 GB cap.

## Decision (details in the ADR)
Container GPU on the existing 24 GB machine; **Qwen3-30B-A3B Q4_K_M** as the default main model with **gpt-oss-20b** as the alternate to settle in the P9-07 eval (it generates 1.2–1.4× faster and is 6.5 GB smaller, which matters under host memory pressure; its briefing time is not comparable); no separate small model and no dense 14B; nomic-embed-text for embeddings; **start on demand** with a health-driven readiness wait (budget 5 minutes; the slowest measured load was 105 s); prompts ≤ 8K tokens; host-native llama.cpp is the documented fallback (endpoint change only; 1.1–1.4× faster generation, loads in 5–10 s even under pressure).

## Sketch of the `xd-llm` service (for P9-01 — not wired into compose yet)
```yaml
xd-llm:
  profiles: [ai]
  image: quay.io/ramalama/ramalama:0.25.0            # digest sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e
  devices: ["/dev/dri:/dev/dri"]                      # GPU through libkrun (Vulkan "Venus")
  command: [llama-server, --host, 0.0.0.0, --port, "8080", -m, /mnt/models/Qwen3-30B-A3B-Q4_K_M.gguf,
            -c, "16384", -ngl, "999", -np, "1", --jinja, --no-webui]
  volumes: ["xd-models:/mnt/models:ro"]                # or the host folder; no load-time difference was demonstrated
  networks: [back]                                    # no published port: xd-api reaches it by name
  mem_limit: 8g                                       # UNTESTED guess: VM "used" peaked at 5.3 GB in the benchmark; measure in P9-01
```
Hardening (`read_only`, `cap_drop`, non-root) must be tested against `/dev/dri` access in P9-01.
