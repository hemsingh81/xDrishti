# P9-01 — `xd-llm` service, model runtime and the LLM client

- **Status:** Draft (written 2026-10-24; decisions below were researched under the owner's delegation; the owner's Approved is still needed before an agent builds it)
- **Phase / roadmap row:** P9 — AI assistant, slice P9-01 of [P9-00](P9-00-ai-assistant-plan.md)
- **Design refs:** docs/design/14-ai-assistant.md §2, §3 · 17-deployment-and-operations.md §1 to §4 · 19 §4 (LLM down, secrets) · ADR 0005 · docs/research/p0-15-llm-benchmark.md ("Sketch of the `xd-llm` service") · deploy/CLAUDE.md
- **Needs:** ADR 0005 (Proposed; container runtime assumed), the models already on the host (`main`, `gptoss`, `embed` in `~/Library/Application Support/xDrishti/models`, P0-15)
- **Size:** 2 agent-days (compose and script 1, client and endpoint and tests 0.5, measurements and docs 0.5)
- **Safety:** yes. A container with GPU device access and a new secret. It publishes no port, has no egress, reads no secret but its own key, and nothing starts it except the owner's command.

## Goal
The owner can start a local model server with one command, see in the app whether it is down, loading or ready, and the API has a typed chat client for it, with the memory limit, load time and effect on the API and worker measured instead of guessed, and with everything else (nightly plan, API health) independent of whether the model server exists.

## Scope
**In:** compose service `xd-llm` (profile `ai`); `scripts/xd-ai.sh` (`start [main|gptoss]`, `stop`, `status`, `logs`); `xd-up.sh`/`xd-down.sh` changes (the key secret, `XD_MODELS_DIR`, `down` includes profile `ai`); `scripts/check-ai-compose.sh` (static rules, called by `scripts/check.sh`); `Application/Abstractions/Ai/ILlmHealth` + `LlmStatus`; `Infrastructure/Ai/Llm` (`LlmOptions`, `HttpLlmHealth`, the `IChatClient` registration); `GET /api/assistant/llm-status`; the package bump; the measurements; doc 17 and ADR 0005 text updates.
**Out:** the MCP server (P9-02), the chat endpoint, the tool loop, prompts, audit (P9-03), any screen (P9-04), downloading a new model (Open questions), embeddings (P9-00 D5), starting the container from the app (P9-00 D7).

## Acceptance criteria
1. Given the stack started by `deploy/xd-up.sh` (no `ai` profile), when containers are listed then there is no `xd-llm`; `xd-up.sh` behaves as before except it creates the Keychain item `xdrishti/llm/api-key` (32 random characters, never replaced) on first run and writes the secret file `llm_api_key`.
2. Given `scripts/check-ai-compose.sh`, when it reads `deploy/compose.yaml` then it fails unless `xd-llm`: has profile `ai` only, no `ports`, networks exactly `[back]`, an image with tag `0.25.0` **and** digest `sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e`, `cap_drop: [ALL]`, `no-new-privileges`, a `mem_limit`, a healthcheck, the models mount read-only, exactly one secret (`llm_api_key`), no `environment` value that looks like a secret. A planted violation of each makes it fail (one test per rule).
3. Given the model file is present, when `scripts/xd-ai.sh start main` runs then the container becomes healthy within the 300 s budget and the script prints `ready: xd-main, context 16384, <seconds> s`; running it again changes nothing; `start gptoss` switches the model (stops, starts); `stop` removes the container; `status` prints container state, health, model alias, the host swap in use (`sysctl vm.swapusage`) and the VM's free memory.
4. Given the model file is missing, when `start` runs then exit 2 and the message names `scripts/llm-models.sh <name>`; nothing is pulled or downloaded by the script (the server itself is started with `-m` and never `-hf`).
5. Given `xd-llm` is running, when a request reaches `/v1/models` without the key then 401; with the key 200; `podman port xd-llm` is empty; from the host no 127.0.0.1 port answers; `/health` answers 503 while loading and 200 when ready (recorded).
6. Given `ILlmHealth`, when the endpoint is refused or the name does not resolve then state `Down`; 503 `Loading`; 200 with a matching model `Ready`; `/v1/models` 401 `Misconfigured` ("the API key was rejected"); a 3 s timeout `Down` ("no answer"). The detail text is from a fixed vocabulary, never an exception message, a URL or the key. Result cached 5 s. *Fake-handler unit tests.*
7. Given a session, when `GET /api/assistant/llm-status` is called then `{state, model, modelFile, modelSha256, build, checkedAt, detail, startCommand}` where `startCommand = "scripts/xd-ai.sh start"`; `modelFile` is the base name of `model_path` from `GET /props`, `modelSha256` is looked up in `Assistant:Llm:KnownModels` (file name -> SHA-256; defaults hold the two pinned files of `scripts/llm-models.sh`) and is null for an unknown file, `build` is `build_info` from `/props` (E2's P9-06 to P9-09 store model id and sha per result); 401 without a session; the response never contains the key, the path or the base URL.
8. Given `Assistant:Llm:BaseUrl` points nowhere, when `xd-api` starts then it is healthy (`/health/ready` 200), `GET /api/system/status` is unchanged, and the nightly pipeline tests pass: no health check, hosted service or Planning code references `Application/Ai` or `Infrastructure/Ai` (architecture test).
9. Given a running server, when the explicit test `LlmSmokeTests` (`[Fact(Explicit = true)]`, not run by `check.sh`) runs then a streamed completion yields at least 5 text deltas, 10 of 10 tool-call round trips with a two-tool schema produce valid arguments, and no `<think>` text appears with thinking off. Results written to the measurement note.
10. Measurements (below) are recorded in `docs/design/17` §1/§2 and as an amendment of ADR 0005; `mem_limit` is set to the measured peak x 1.5 rounded up to 1 GB.
11. Given the package bump, when `scripts/check.sh` runs then it is green with `Microsoft.Extensions.*` at 10.0.13 and the new packages pinned exactly.
12. Given the key, when options are logged or `ToString`-ed then it is absent (test); it is mounted only into `xd-llm` (as a file) and `xd-api` (`Assistant__Llm__ApiKey`).

## Contract
- **Compose (`deploy/compose.yaml`):**
```yaml
xd-llm:
  profiles: [ai]
  image: quay.io/ramalama/ramalama:0.25.0@sha256:d60dfda3113e127071d2b2679921c763d0eb7a0733596f68bf93a4ef1ca1216e
  restart: "no"                      # on demand (ADR 0005)
  devices: ["/dev/dri:/dev/dri"]     # GPU through libkrun (Vulkan "Venus")
  command: [llama-server, --host, 0.0.0.0, --port, "8080", -m, "/mnt/models/${XD_LLM_MODEL_FILE:?}", --alias, "${XD_LLM_ALIAS:-xd-main}",
            -c, "${XD_LLM_CTX:-16384}", -ngl, "999", -np, "1", --jinja, --no-webui]
  environment:
    LLAMA_ARG_API_KEY_FILE: /run/secrets/llm_api_key
    LLAMA_ARG_CHAT_TEMPLATE_KWARGS: ${XD_LLM_KWARGS:?}   # JSON text exported by xd-ai.sh (braces break a ":-" default)
  secrets: [llm_api_key]
  volumes: ["${XD_MODELS_DIR:?}:/mnt/models:ro"]
  networks: [back]
  mem_limit: 8g                      # PLACEHOLDER until measured (AC 10)
  healthcheck: { test: [CMD, curl, -fsS, http://127.0.0.1:8080/health], interval: 10s, timeout: 3s, start_period: 150s, retries: 20 }
  read_only: true
  tmpfs: [/tmp]
  cap_drop: [ALL]
  security_opt: [no-new-privileges:true]
```
`curl` in the image, the flag names and the hardening are **UNVERIFIED**: P9-01 checks `llama-server --help` of the pinned image and `curl --version`, and if `--api-key-file`/`--chat-template-kwargs` are missing uses the env names of the README (`LLAMA_ARG_*`, llama.cpp `tools/server/README.md`, read 2026-10-24). If non-root + `read_only` cannot open `/dev/dri/renderD128`, try `group_add: [keep-groups]`, then `user: root` with `cap_drop: [ALL]` (documented exception in doc 17, still no egress). The healthcheck, if `curl` is absent, uses the image's `wget` or a `bash` `/dev/tcp` probe.
- **`xd-api` additions:** secret `llm_api_key` -> `Assistant__Llm__ApiKey`; env `Assistant__Llm__BaseUrl: http://xd-llm:8080/v1`. Top-level `secrets.llm_api_key.file: ${XD_SECRETS_DIR}/llm_api_key`.
- **Scripts:** `xd-up.sh` exports `XD_MODELS_DIR` (default `$HOME/Library/Application Support/xDrishti/models`) and writes `llm_api_key` with `write_secret`; `xd-down.sh` adds `--profile ai` to `down`; `scripts/xd-ai.sh` maps `main` -> `Qwen3-30B-A3B-Q4_K_M.gguf`/kwargs `{"enable_thinking":false}`, `gptoss` -> `gpt-oss-20b-MXFP4.gguf`/kwargs `{}` (a reasoning model; effort left at its default), exports `XD_LLM_MODEL_FILE`, `XD_LLM_ALIAS=xd-main`, `XD_LLM_CTX=16384`, `XD_LLM_KWARGS`, then `podman compose -f compose.yaml --profile ai up -d --wait --wait-timeout 330 xd-llm`.
- **Options (`Assistant:Llm`, `[Required]`/`[Range]`, `ValidateOnStart`):** `BaseUrl` (default `http://xd-llm:8080/v1`), `ApiKey` (from the secret file), `Model` (`xd-main`), `RequestTimeoutSeconds` 180 (10..600), `HealthTimeoutSeconds` 3 (1..10), `ReadyWaitSeconds` 300 (30..900), `StatusCacheSeconds` 5, `KnownModels` (dictionary file -> SHA-256: `Qwen3-30B-A3B-Q4_K_M.gguf` -> `0d003f66...a8f5`, `gpt-oss-20b-MXFP4.gguf` -> `27cd6c43...5901`, full values copied from `scripts/llm-models.sh`).
- **Code:** `Application/Abstractions/Ai/ILlmHealth.GetStatusAsync(ct) -> LlmStatus(State, Model, ModelFile, ModelSha256, Build, CheckedAt, Detail)`, `LlmState { Ready, Loading, Down, Misconfigured }`; `Infrastructure/Ai/Llm/HttpLlmHealth` (typed `HttpClient`, no redirects, no cookies; `/health` without key, then `/v1/models` with key); `LlmClientRegistration` registers `IChatClient` as `new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = baseUrl }).GetChatClient(model).AsIChatClient()` (extension name UNVERIFIED in 10.11.0). The function-invoking wrapper is P9-03's.
- **API:** `GET /api/assistant/llm-status` -> `LlmStatusView {state: "ready|loading|down|misconfigured", model: string|null, modelFile: string|null, modelSha256: string|null, build: string|null, checkedAt, detail: string, startCommand}`; tag `Assistant`; operation id `GetLlmStatus`; `scripts/gen-api.sh`.
- **Packages (`Directory.Packages.props`, exact):** `Microsoft.Extensions.AI` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0 (both nuget.org, released 2026-10-14, read 2026-10-24), the `OpenAI` package at the version those resolve to (>= 2.14.0; pin what restore selects, UNVERIFIED); the `Microsoft.Extensions.*` set from 10.0.12 to **10.0.13** (M.E.AI requires `>= 10.0.13`; the runtime image is already 10.0.13).
- **Log events (block 6000 to 6019):** 6000 status check result (state only), 6001 key missing at start (error, not the value), 6002 status check failed (exception type only). Never the URL.
- **Data:** none. No migration.

## Measurements (each into a table in doc 17 and ADR 0005's amendment; same harness as P0-15, `scripts/llm-bench.sh`, `scripts/llm-mem.sh`)
| # | What | How | Decides |
|---|---|---|---|
| M1 | Peak container memory and VM "used"/"available" with `main` loaded and an 8K prompt generating; host swap before and after | `podman stats` + `llm-mem.sh` every 5 s, 3 runs | `mem_limit` (peak x 1.5, 1 GB steps) |
| M2 | Load time with a memory sample at each load (P0-15 left the 22 to 105 s cause open) | 5 cold starts, host folder mount, sample host swap and VM cache each | whether `ReadyWaitSeconds` 300 stays; whether the volume `xd-models` is worth keeping (owner act) |
| M3 | Effect on API and worker: p95 of 200 `GET /api/system/status` and 200 `GET /api/plans` and the run time of the explicit benchmark `Timeframe_builders_bars_per_second` under three conditions: model stopped, loaded and idle, generating | by script, 3 repeats each | whether P9-00 D13 (no chat during nightly) stays. Rule: if every p95 and run time is within 20 % and 25 % of "stopped", D13 is relaxed to a warning; otherwise it stays and E2's jobs run outside 17:30 to 20:00 IST |
| M4 | Prompt cache effect: first-token time of the second turn with the same 3K-token prefix (system prompt + tool schemas) vs the first | 5 pairs on `main` | whether the budget of 8K prompt tokens can be loosened |
| M5 | Hardening result: which of non-root/`read_only`/`keep-groups`/root-without-capabilities opens the GPU | try in that order | the compose `user`/`group_add` lines |
| M6 | `llama-server --version`, the build number, the flags accepted | inside the pinned image | doc 17 "llama.cpp build in the image" |

## Non-functional
Start budget 330 s (slowest measured load 105 s); status check <= 3 s and cached 5 s; the API never blocks on the model server (no call at startup, none in health). Security: key only as a file secret, compared by llama.cpp; no port published, `back` is `internal: true` so the server has no route out; models read-only; no `-hf`. Observability: log ids above; `xd-ai.sh status` is the operator's view.

## Test plan
Unit: `HttpLlmHealth` over a fake handler for each state, timeout, cache, vocabulary (no URL/exception text), options validation, key redaction. Integration (`WebApplicationFactory`): endpoint 200/401, API healthy with an unreachable URL, system status unchanged. Architecture: Planning and health code do not reference `Ai`; `Infrastructure/Ai` is the only place that references `OpenAI`. Static: `check-ai-compose.sh` with one planted-violation test per rule. Manual/explicit (recorded, not in `check.sh`): `LlmSmokeTests`, M1 to M6, `xd-ai.sh` start/stop/status/idempotence on the real machine.

## Decisions (researched)
**Host folder, not the volume.** The models mount read-only from `XD_MODELS_DIR`; P0-15 found no load-time difference (22 to 105 s either way, cause open), and the volume duplicates 18.6 GB of the 120 GB VM disk. The volume is left alone (deleting data is the owner's act, Open questions). Rejected: copy into a volume.
**Tag and digest together.** The repo rule is exact tags; P0-15 ran by digest. `0.25.0@sha256:d60dfda3...` satisfies both; quay.io on 2026-10-24 shows 0.25.0 (2026-09-25) as the newest and equal to `latest`.
**`restart: "no"` and no `depends_on`.** On demand means a stopped server stays stopped; the API must not wait for it.
**Context 16384, `-np 1`, prompt caching left on.** ADR 0005 values; with one slot the shared prefix (system prompt + tool schemas) is reused between turns, which P0-15 did not measure (`cache_prompt` was off); M4 measures it.
**Thinking off by server flag, per model.** Per-request `chat_template_kwargs` exists but needs raw request fields through the OpenAI client; a startup kwargs value keeps the client plain (llama.cpp README, 2026-10-24). `gptoss` always reasons; its kwargs stay empty and its reasoning text is never shown or stored.
**Status without a container socket.** The state comes from HTTP health only, so the app needs no rights over containers. Rejected: asking Podman from the API.
**No `/metrics`, `/slots` or `/props`.** `--no-slots` is not set (default on) but nothing calls them; the server is on a private network.
**Alias constant `xd-main`.** Switching the model file changes nothing in the client; the stored `model_id` (P9-03) is read from `/v1/models` at conversation start, which tells which file served it.

## Risks and honest limits
1. The compose sketch's `curl`, flags and hardening are unverified on the pinned image; the measurements may force the root-without-capabilities exception.
2. `mem_limit` can be too low once the weights count against the container (GPU memory inference only); M1 sets it from data, and a kill shows as `Down`.
3. Host swap (28 to 42 GB) means a load can take 105 s or more; the status says `loading` and nothing else waits.
4. A chat during the model's load is refused by P9-03 (503 `assistant.model_unavailable`), not queued.
5. Two sessions of the owner would share one slot; P9-03 refuses the second with `assistant.busy`.

## Open questions
None that block the build. Owner acts afterwards: start the server; optionally remove the duplicate volume `xd-models` (`podman volume rm xd-models`, frees 18.6 GB). Default: both left as they are.

## Done when
- [ ] All acceptance criteria have evidence; M1 to M6 recorded
- [ ] `scripts/check.sh` green; reviewer agent: no blockers/majors
- [ ] Contract regenerated (`scripts/gen-api.sh`); no migration
- [ ] Doc 17 (§1, §2, §3, §4 rows for `xd-llm`, the secret, the new network facts), ADR 0005 amended (status of the container choice, "P9-09 eval"), `deploy/README.md` and `scripts/README.md` updated; `scripts/build-docs.sh`

## Review fixes 2026-10-25
1. Package-bump fact restated: `Directory.Packages.props` pins `Microsoft.Extensions.*` 10.0.12 while the Dockerfile runtime image is 10.0.13; the bump to 10.0.13 is this slice's (AC 11). Highest existing log id is 4801; the P9 block 6000 to 6199 is unchanged. Next free ADR number is decided at build time (0102 on 2026-10-25).
