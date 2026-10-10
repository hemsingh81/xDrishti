# P0-15 — xd-llm GPU benchmark under libkrun

- **Status:** Done
- **Phase / roadmap row:** P0 — Foundations & data trial (feeds P9-01, P9-02, P13-01)
- **Design refs:** docs/design/14-ai-assistant.md §2–3, §5; 17 §1–2; 03 §2.5; decision Q8 (machine size)

## Goal
Measure, on this Mac, how fast the candidate local models really run and how much memory they need — inside the
libkrun Podman machine with GPU, and natively on the host — so we can **decide the runtime, the models and the
Podman machine size** before P9 builds anything on top of `xd-llm`. The deliverable is evidence and a decision, not a
production service.

## What is already known (checked read-only on 2026-10-09)
- Host: Mac16,7 (Apple M4 Pro), 48 GB unified memory, ~200 GB free disk.
- Podman machine: libkrun, 10 CPUs, **24 GiB RAM**, 120 GiB disk; a GPU render node (`/dev/dri/renderD128`) is visible inside the VM.
- Not installed: `ramalama`, `llama-server` (llama.cpp). No model has been downloaded.
- Design assumption to test: a ~30B-class 4-bit model (≈ 18–22 GB) does **not** fit beside the stack in 24 GB, so the
  VM must grow (to ~32 GB) or the model must run host-native (doc 14 §2 fallback, "endpoint change only").

## Scope
**In:** installing the benchmark tooling (pinned), downloading approved model files, running the benchmark in the container
GPU path, the host-native path and a CPU baseline, a small capability smoke test, a memory/interference study, the
findings document, the decision (ADR) and the doc updates.
**Out:** the real eval set and prompt design (P9-07), the `xd-mcp` tools (P9-02), the assistant endpoint/UI (P9-03),
wiring `xd-llm` into `deploy/compose.yaml` for real use (P9-01 — only a *sketch* of the service definition is produced here),
any fine-tuning, any use of cloud LLMs.

## Candidates (proposed — confirm in the open questions)
| Role | Candidate | Quantisation | Approx. size* | Why |
|------|-----------|--------------|---------------|-----|
| Main | Qwen3-30B-A3B (MoE, ~3B active) | Q4_K_M | ≈ 18–19 GB | Named in doc 14; MoE gives speed of a small model |
| Main (alt) | a dense ~14B instruct model (e.g. Qwen3-14B) | Q4_K_M | ≈ 9 GB | Fits easily; shows the dense-vs-MoE trade-off |
| Main (alt) | gpt-oss-20b (MoE) | native MXFP4 / Q4 | ≈ 12 GB | Strong tool calling at modest size |
| Small | Qwen3-8B (or a 7–14B equivalent) | Q4_K_M | ≈ 5 GB | Classification, cheap tasks |
| Embeddings | nomic-embed-text v1.5 (alt: bge-m3) | F16 / Q8 | < 1 GB | Search over notes/docs |

*Sizes are estimates from memory; exact files, sizes, licences and SHA-256 are pinned in the findings **before** download.
At benchmark time the list is re-checked for newer open models in the same size classes (at most 3 main candidates).

## Approach
1. **Environment record:** versions (macOS, Podman, libkrun/krunkit, VM kernel), VM size, GPU visibility; inside a container confirm
   the Vulkan device is detected and the server log shows layers offloaded to the GPU (not a silent CPU fallback).
2. **Tooling (pinned, with approval):** RamaLama for the container path (as designed) and the plain llama.cpp server image with
   Vulkan as the cross-check; a native llama.cpp (Metal) build or release for the host path. The benchmark only needs the
   OpenAI-compatible endpoint, so it is runtime-agnostic.
3. **Harness:** a small .NET spike `backend/spikes/XDrishti.LlmBench` (+ Tests) driven by `scripts/llm-bench.sh`: streams
   `/v1/chat/completions`, records time-to-first-token and per-token timing, reads llama.cpp's own `timings`, repeats runs, writes
   `data/llm-bench/results.json` and a Markdown report. Pure logic (stream parsing, percentile stats, report) is unit-tested.
4. **Runs:** one model at a time, on AC power, sequentially; warm and cold start; 3 repeats; contexts 1K / 8K / 32K (main model).
5. **Memory & interference:** repeat with the real stack up (`deploy/xd-up.sh`) and with synthetic CPU load standing in for ML.NET training.
6. **Decision:** apply the thresholds below, write findings + ADR, update docs 14/17 and settle Q8.

## Acceptance criteria
1. **Environment and GPU use proven.** Versions and VM size are recorded; for the container path the log shows the Vulkan device and
   full layer offload; throughput on GPU is compared with a CPU-only baseline for the small model, so a silent CPU fallback cannot pass.
2. **Artifacts pinned and approved.** Each model file is listed with source URL, size, licence and SHA-256 and is downloaded only
   after the owner approves that item; files live in a named volume (container) and a folder outside the repo (host); nothing large is committed.
3. **Throughput measured** for every candidate × runtime: model load time, prompt-processing tokens/s and generation tokens/s at
   1K / 8K / 32K context (32K for the main model), time-to-first-token for a briefing-style prompt (~3–4K tokens in, ~300 words out),
   median and range of 3 repeats, warm and cold.
4. **Memory headroom measured.** Peak memory of the VM and the host with each model loaded and with the xDrishti stack running; no OOM
   kill or heavy swapping; `/api/system/status` stays healthy during generation; the VM size needed (24 GB vs 32 GB) is stated.
5. **Interference measured.** Generation speed with and without a CPU-heavy job running; a scheduling policy is proposed
   (e.g. LLM jobs outside the nightly pipeline window, or a concurrency limit).
6. **Capability smoke passed** on the main candidates: streaming works; tool calls come back as valid JSON for a sample tool schema
   in ≥ 9 of 10 trials; JSON/structured output works; the embeddings endpoint returns vectors of the expected dimension; a needle-in-32K
   prompt is answered correctly. (Full quality evaluation is P9-07, not this story.)
7. **Decision recorded:** runtime (container GPU vs host native), main / small / embedding models with quantisation and context length,
   Podman machine memory and disk, models-volume policy, and a sketched `xd-llm` service definition (profile `ai`); ADR added; doc 14 §2
   and doc 17 §1–2 updated; **Q8 settled**; plan story P9-01 adjusted if the decision changes its scope.
8. **Safe and reversible.** Original VM settings are recorded before any resize; a resize (needs stopping the stack) happens only with the
   owner's approval and ends with `deploy/xd-up.sh` and a green System status; benchmark servers bind to 127.0.0.1; no tokens or secrets are
   needed or requested (public models only); disk use stays ≤ 60 GB and unneeded models are removed at the end.
9. **Reproducible.** `scripts/llm-bench.sh` re-runs the benchmark from the pinned artifacts; unit tests cover the stream parser, statistics
   and report; `scripts/check.sh` stays green.

## Proposed decision thresholds (owner may change)
| Measure | Target for the chosen main model |
|---------|----------------------------------|
| Generation speed at 8K context | ≥ 15 tokens/s |
| Time-to-first-token, 4K-token prompt | ≤ 15 s |
| 300-word briefing, end to end | ≤ 45 s |
| Load from warm disk | ≤ 60 s |
| Memory | stack + model fit with ≥ 4 GB free in the VM, no swapping on the host |
| Tool-call validity | ≥ 9 / 10 |

If no container-GPU configuration meets them, the host-native path is benchmarked as the answer (endpoint change only). If neither does,
the decision is a smaller main model, recorded with the numbers.

## Contract
- **API / Data / UI / Jobs:** none in the product. Benchmark endpoints are temporary, on 127.0.0.1.
- **Files (expected):** `backend/spikes/XDrishti.LlmBench*`, `scripts/llm-bench.sh`, `docs/research/p0-15-llm-benchmark.md`,
  `docs/adr/00NN-local-llm-runtime-and-models.md` (next free number), updates to docs 14 and 17 (+ `scripts/README.md`, slnx).
- **Machine changes (each needs approval):** install RamaLama (pinned) and a native llama.cpp; download the approved models (≈ 35–45 GB in
  total for the default candidates); optional Podman machine resize to 32 GB.

## Non-functional
- Heavy load: runs make the Mac hot and busy; thermals/throttling are noted, runs happen sequentially, and the owner can pause at any time.
- Time-boxed: expected effort is larger than the 3 pts in the plan (mostly waiting for downloads and runs) — see open questions.
- Licences recorded per model; personal local use only.

## Test plan
- **Unit (offline):** SSE/stream parsing incl. partial chunks, token-timing maths, percentile/median statistics, report rendering, threshold evaluation.
- **Live (agent-run, no credentials):** `scripts/llm-bench.sh` against each runtime → `results.json` + report; stack health checks via `/api/system/status`.
- Findings cross-checked once by repeating one configuration a second time to show run-to-run variance.

## Decisions (owner, 2026-10-09)
Q1–Q6: accepted defaults — downloads approved item by item (exact list in the research doc), tooling install and VM resize approved (resize turned out unnecessary), thresholds as proposed, run immediately, effort 5 pts.
Outcome notes: the "no swapping on the host" threshold could not be met (the Mac was already swapping 28–42 GB) and the ≤ 60 s load threshold held only in some conditions (22–105 s); both are recorded in the research doc and drive the on-demand decision. A repeat of one configuration (30B container) differed by 4–8%.

## Open questions (answered)
1. **Downloads:** approve the default candidates above (≈ 35–45 GB total from a public model hub), or change the list? I will still ask per file with exact size and source before downloading.
2. **Tooling install:** OK to install RamaLama and a native llama.cpp on this Mac (pinned versions, user-local, no sudo)?
3. **VM resize:** OK to grow the Podman machine from 24 GB to 32 GB for the test (stops the stack for a few minutes; I restart it with `deploy/xd-up.sh`)?
4. **Thresholds:** accept the table above, or set your own?
5. **Timing:** run during the day while you work (the Mac will be slow), or in a window when you are away? Default: a window you name.
6. **Effort:** accept that this is likely 5 pts rather than 3 (I will update the plan)?

## Done when
- [x] All acceptance criteria have evidence (results.json, findings document)
- [x] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [x] Decision recorded as an ADR; docs 14/17 updated; Q8 settled
- [x] Plan updated: P0-15 done; P9-01 adjusted if needed
