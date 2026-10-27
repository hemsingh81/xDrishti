# 0005 — Local LLM: container GPU runtime, MoE model, on-demand

- **Status:** Proposed — the owner is asked to confirm the container-vs-native choice (see Context)
- **Date:** 2026-10-10

## Context
Doc 14 assumed a ~30B-class 4-bit model served by llama.cpp in a libkrun Podman machine, with host-native as a fallback, and doc 17
assumed the machine would have to grow to ~32 GB. Story P0-15 measured the candidates on this Mac (M4 Pro, 48 GB) — see
[docs/research/p0-15-llm-benchmark.md](../research/p0-15-llm-benchmark.md).

The spec said that if no container configuration met every threshold, host-native would be the answer. The container met all speed thresholds but not
"load ≤ 60 s" in every condition (22–105 s) nor "no host swapping" (the Mac already swaps 28–42 GB; native does not meet that either); native loads in 5–10 s.
The container is proposed anyway because it keeps `xd-llm` inside the compose stack (isolation, network policy, one start/stop path) and the speed is sufficient; this is a deliberate deviation.

## Decision
- **Runtime:** llama.cpp server in a container on the existing libkrun machine, GPU via Vulkan/"Venus" (`/dev/dri`). Image:
  `quay.io/ramalama/ramalama:0.25.0` pinned by digest, run by our own compose service (not the `ramalama` CLI, which publishes on all interfaces).
  Host-native llama.cpp (Metal; 1.13–1.41× faster generation, 1.4–1.8× faster prompt reading, loads in 5–10 s) stays the documented fallback — an endpoint change only.
- **Machine size:** unchanged, **10 CPUs / 24 GB / 120 GB**. Model weights live in GPU memory outside the VM; measured VM peak with the 30B loaded and the stack running was 5.3 GB.
  (Settles Q8; revisit before tick-data storage in P13.)
- **Models:** main = **Qwen3-30B-A3B Q4_K_M** (MoE, Apache-2.0); alternate = **gpt-oss-20b MXFP4** (Apache-2.0, 1.2–1.4× faster generation, 12 GB), final choice by the P9-07 eval set;
  **no separate small model** (the MoE is faster than the dense 8B) and the dense 14B is rejected (first token 27.5 s > 15 s). Embeddings: **nomic-embed-text v1.5 Q8_0**, CPU.
- **Storage:** model files come from a pinned, checksum-verified download script on the host; the service may read them from the host folder or from a Podman volume (`xd-models` holds a copy of the 30B) — re-measured load times (22–105 s) depend on host memory pressure, not on the mount.
- **Operation:** profile `ai`, started on demand (not always-on) because the host is already swapping heavily (28–42 GB of swap in use in the sampled runs; the "no host swapping" threshold could not be met) and a 30B load takes 22–105 s (cause not established) — callers wait for health (budget 5 minutes; slowest measured load 105 s); one request at a time (`-np 1`); context 16K by default (32K supported);
  prompts budgeted to ≤ 8K tokens — reading a 30K-token prompt takes 2.5–4 minutes; no published port (reached by name on the `back` network).
- **Security:** nothing in this path needs credentials; every benchmark server except RamaLama's own first `serve` (0.0.0.0 for about two minutes, no data) was bound to 127.0.0.1; the LLM never decides trades (doc 14 §1).

## Consequences
- P9-01 builds the `xd-llm` compose service from the sketch in the research doc; hardening against `/dev/dri` access must be verified then.
- The LLM's effect on the API and worker (the direction that matters for the nightly pipeline) was not measured; P9-01 must check it, and must measure the service's real memory limit.
- The nightly pipeline must not depend on the LLM; LLM jobs (briefing) run outside the nightly window or when the model is already loaded.
- If the host memory situation worsens or the eval prefers gpt-oss-20b, the smaller model is a config change, not a redesign.
- A newer open model can be evaluated with `scripts/llm-bench.sh` without code changes.

## Amendments
- **2026-10-27 (P9-01, [ADR 0136](0136-the-local-model-server-runs-in-a-container-on-demand-with-the-measured-qwen3-30b-a3b-as-default-two-pinned-alternatives-and-a-status-the-api-reads-over-http-only.md)).** Status of the container choice: built as a compose service `xd-llm` (profile `ai`, on demand), still **Proposed** until the owner confirms. The decision text above is unchanged. Pinned model files (with source revision, size, SHA-256, licence), the image's inside facts and the hardening result are in ADR 0136; a third candidate `qwen-instruct` (Qwen3-30B-A3B-Instruct-2507) is pinned there, not yet measured. "P9-07 eval" reads **P9-09 eval** (P9-07 is the Lab proposal slice). The memory limit, load-time, interference and prompt-cache measurements this ADR demanded of P9-01 were **not taken** in the build (they need the model loaded); they are listed in ADR 0136 item 8 and remain open.
