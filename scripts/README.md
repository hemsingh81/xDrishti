# scripts

Developer helpers (macOS). They use the .NET SDK in `~/.dotnet` and Podman Desktop.

| Script | What it does |
|--------|--------------|
| `check.sh` | **All quality gates**: backend format/build/tests (incl. integration tests on Podman), frontend typecheck/lint/format/tests/build. Must pass before every commit. |
| `gen-api.sh` | Regenerates the OpenAPI contract and the frontend's typed API client. Run after any API change. |
| `dev-api.sh` | Runs the API on the host with hot reload (port 5080) against the containerised database; reads the DB password and the broker vault key from the Keychain (start the stack once first). |
| `bench-coldstart.sh` | P3-02 / ISS-12: cold-start figures of the rules compiler, N fresh processes (default 15) of the explicit compile benchmarks, min/median/max: the very first compile of a process (reported), the first compile after `CompilerWarmup.Run` (≤ 20 ms) and the warm compile (≤ 3 ms); exit 1 when a median is over its target. Not part of `check.sh` (needs a quiet machine); run before a release and when the Strategies or Indicators libraries change. |
| `build-docs.sh` | Rebuilds the HTML docs (`docs/site`) from `docs/design/*.md`. |
| `_fingerprint.sh` | Internal: hash of uncommitted code changes; `check.sh` stores it after a green run and the Stop hook compares (ADR 0004). |
| `auth-secrets.sh` | P1-01: store or change the owner's sign-in e-mail, name and password in the Keychain (hidden password prompt); then `deploy/xd-up.sh` (first start) or `deploy/xd-up.sh --owner-reset` (change password). |
| `dhan-secrets.sh` / `dhan-trial.sh` | P0-13 only: store Dhan credentials in the Keychain, then run the read-only Dhan Data API trial (report in `data/trial/`). |
| `llm-setup.sh` / `llm-models.sh` | P0-15: install the pinned native llama.cpp and the RamaLama image; download the pinned models (SHA-256 verified, resumable). |
| `llm-serve.sh` / `llm-wait.sh` / `llm-mem.sh` | P0-15: start/stop a local LLM server (native, CPU or container; all on 127.0.0.1), time its load, sample memory. |
| `llm-bench.sh` | P0-15: benchmark an OpenAI-compatible local LLM server (throughput, first-token time, capability smoke) → `data/llm-bench/`. |
| `serve.sh` | Serves the clickable prototype (8766) and the docs site (8765). |

The stack itself starts with `deploy/xd-up.sh` and stops with `deploy/xd-down.sh`.

Broker API keys and secrets are not handled by any script: they are entered in the app and stored in the encrypted vault (ADR 0008, `deploy/README.md`).

Frontend dev server (hot reload, proxies `/api` to the stack): `cd frontend && npm run dev` → http://localhost:5173.
