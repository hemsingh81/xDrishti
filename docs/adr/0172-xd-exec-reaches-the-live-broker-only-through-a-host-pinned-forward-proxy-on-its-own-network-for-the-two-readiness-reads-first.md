# 0172 - xd-exec reaches the live broker only through a host-pinned forward proxy on its own network, for the two readiness reads first

- **Status:** Accepted (owner, 2026-10-31: option B; the proxy starts only with `--exec` AND `XD_EXEC_READINESS_LIVE=true`; `api.dhan.co:443` is the only host).
- **Date:** 2026-10-31.
- **Relation to other ADRs:** closes step 5 of ADR 0171 (egress for `xd-exec`) and the "no route out" of ADR 0156 §4 and doc 17 §3. Builds on ADR 0133 (a new broker endpoint enters the allow-list in the commit of the slice that uses it), ADR 0157 and 0158 (one guard table, `Environment=live` refused today), ADR 0170 (readiness reads).

## Context
With ADR 0171 built, `xd-exec` can hold the day's token for a connected account but cannot use it: it sits on the `exec` network, which is `internal` (members `xd-db` and `xd-exec`), and `ExecComposeRules` pins "no route out". Facts that shape the choice:
1. The `egress` network is a plain, non-internal bridge. A container on it can reach any host on the internet. The only host restriction today is in application code (the guard table in `ExecutionEndpoints`, and the data-side allow-list of ADR 0097/0133). `xd-api` and `xd-feed` rely on that and are the only members.
2. `xd-exec` is the process that may one day place orders. A compromise of it with open internet access could send the day's token anywhere; a compromise with a network-level host pin could not.
3. Dhan checks the source IP of order calls against the owner's registered static IP. Calls from the Podman VM leave through the Mac's connection, so the readiness reads (`GET /v2/profile`, `GET /v2/ip/getIP`) over the same path are the honest test of that address (P12-11).
4. The only hosts needed for readiness are the live API host (`api.dhan.co`, port 443). The order-update socket (`api-order-update.dhan.co`) and the order paths are not needed yet.

## Options
- **A. Put `xd-exec` on `egress` like `xd-api` and `xd-feed`.** Smallest change. The host pin stays only in code; a compromised process has the open internet. Rejected for the process that will hold the order power.
- **B. A host-pinned forward proxy (recommended).** A small container `xd-exec-proxy` is the only member of both `exec` and `egress`. `xd-exec` stays on the internal `exec` network, has no route and no DNS of its own, and sends its HTTPS through the proxy (`HTTPS_PROXY`). The proxy allows `CONNECT` to the listed `host:port` pairs only and refuses everything else. The pin is enforced by a second component, independent of our code.
- **C. Reads through the API (the API calls Dhan and `xd-exec` asks the API).** No route for `xd-exec`, but it makes the execution service depend on the API being up at 08:00 and it moves the token use to the API; the point of readiness is to prove what `xd-exec` itself can do. Rejected.
- **D. Do nothing; live readiness stays `unavailable`.** Honest and safe; the morning stays manual and the phase check can never count a session as proved. The fallback if B is declined.

## Decision (proposed): B, in two steps
1. **The proxy is a new container in profile `live`** (never started by a plain `xd-up.sh`, only with the owner's `--exec`). Image pinned by exact tag, chosen in the build slice after checking it can restrict `CONNECT` to a named host and port list; `read_only`, `cap_drop ALL`, small memory limit, health check, no published port, no secrets, no database access. Networks: `exec` and `egress` only.
2. **The allow-list is a file in the repo, one line per `host:port`,** starting with `api.dhan.co:443` only. Adding a host is a reviewed commit and an ADR line (ADR 0133 rule), and a compose-rule test fails if the list contains any host not named in an accepted ADR.
3. **`xd-exec` gets the proxy by an environment variable, only in live with `Execution__ReadinessLiveEnabled` on.** It never joins `egress`; `ExecComposeRules` keeps "xd-exec is not on egress" and gains "only `xd-exec-proxy` is on both `exec` and `egress`", "the proxy has no secret, no database access and no published port", and "the allow-list file contains exactly the accepted hosts", each with a planted violation.
4. **Code side:** the live guard table gains exactly two rows, `GET /v2/profile` and `GET /v2/ip/getIP` on the live host, nothing else (setIP, modifyIP, RenewToken, orders and every other path stay refused); the live facts class gets an HTTP client with the proxy and the guard handler, still no credentials interface for the order client. A live order path remains impossible: it needs its own ADR that widens both the guard and the proxy list.
5. **The owner act is explicit:** setting `XD_EXEC_READINESS_LIVE=true` and starting with `--exec` is the only way the route exists; a plain deploy never creates it. Turning it off removes the proxy at the next deploy.
6. **Verification before trust:** the live host string is verified against Dhan's documentation by the owner's smoke run (the sandbox host is still UNVERIFIED in the project); until that run is recorded in the runbook the live rows stay behind the same default-off switch.

## Consequences
- Live readiness can become real: the checklist, halt auto-clear and phase check can reach READY or MET on live data, with the address check made over the real path.
- One more container and one more file to keep; the network pin is enforced outside our process, so a bug or compromise in `xd-exec` cannot reach any other host from the network side.
- The proxy sees only the host names it forwards (TLS passes through); it holds no token and logs no body.
- The order-update socket and order paths still need a later ADR (live send), which would add `api-order-update.dhan.co:443` and the order paths, and would be the point where the owner decides whether `xd-exec` may send at all.

## Build (2026-10-31)
- **Where the route lives.** `deploy/compose.exec-live.yaml`, an overlay read by `deploy/xd-up.sh` only with `--exec` AND `XD_EXEC_READINESS_LIVE=true` (case-insensitive). A single file cannot express "profile `live` AND a switch": a service with profile `live` would start with every `--exec`, and an always-present `HTTPS_PROXY` (even empty) would contradict step 3. The overlay defines `xd-exec-proxy` (profile `live`) and adds to `xd-exec` only `HTTPS_PROXY=http://xd-exec-proxy:8888` and a health dependency. A deploy without the overlay removes the proxy container by its exact name (`podman rm -f xdrishti-xd-exec-proxy-1`); `--remove-orphans` is NOT used (review F1: it was not proven safe for the profile-gated Hermes and eval containers); a running `xd-exec` keeps its proxy variable until the next `--exec` deploy recreates it and fails closed meanwhile (review F2); `xd-down.sh` always includes the overlay.
- **The proxy.** tinyproxy `1.11.2-r0` on `docker.io/library/alpine:3.22.2@sha256:4b7ce07002c69e8f3d704a9c5d6fd3053be500b7f1c69fc0d80990c2ad8dd412` (the multi-arch index), built from `deploy/exec-proxy/` (Dockerfile, entrypoint), runs as the image's user `tinyproxy`. The entrypoint writes the config to `/tmp` from the read-only `deploy/exec-proxy/allowlist.txt` at every start: one anchored pattern `^host:port$` per line with `FilterURLs On`, `FilterDefaultDeny Yes`, `FilterCaseSensitive Yes`, `FilterType ere`, and one `ConnectPort` per listed port; a malformed or empty list stops the start. Compose: networks `exec` + `egress`, no secret, no environment, no database, no published port, `read_only` with tmpfs `/tmp`, `cap_drop ALL`, `no-new-privileges`, 32 MB, health check `nc -z 127.0.0.1 8888`.
- **Code.** `ExecutionEndpoints` gains `LiveHost = "api.dhan.co"` and a live table of exactly two rows (`GET /v2/profile`, `GET /v2/ip/getIP`; no id, no query, https, default port). The GET machinery moved from `DhanOrderClient` into `DhanReader` (no write, no body), shared by the sandbox client and `LiveBrokerFacts`. `LiveBrokerFacts` builds its own client: `GuardHandler(Live)` over a `SocketsHttpHandler` whose only proxy is `HTTPS_PROXY` (strictly parsed by `ExecOptions.LiveProxy`: plain-HTTP absolute address, nothing else), no redirects, no cookies; own limit 5/s; 401/DH-901 is `token_invalid`; log event 6616 now names the environment (`Dhan live read ...`), never a token, host or address. Switch off: `token_unavailable`; no proxy: `no_live_route`; no token: `token_unavailable`; none of them makes a request. `DhanOrderClient` still throws for any environment but sandbox and no `IExecCredentials` is composed in live.
- **Tests.** `ExecComposeRules` gains nine rules, each with planted violations (`no-proxy-in-base`, `proxy-profile-live`, `proxy-networks`, `one-bridge`, `proxy-no-secret-db-or-port`, `proxy-hardening`, `proxy-image-pinned`, `exec-gets-only-the-proxy-variable`, `allow-list-accepted-hosts`; the last fails on any line but `api.dhan.co:443`), plus the up-script gate. `ExecutionEndpointsTests` pin the two live rows and fifty refused live requests, and an end-to-end test through `GuardHandler(Live)` with a stub socket lets only the two GETs reach it. `LiveBrokerFactsTests` cover every refusal, the reads, the retry, the token rule, the limit and the audit with a stub socket. The architecture tripwires now pin the two hosts and the two live rows in the one guard file instead of "no live host" (the ADR's change), plus: the proxy setting is read in one file, `UseProxy = true` exists only in the live reads, and the overlay adds only the proxy to `exec`.

## Verification (2026-10-31, Podman 6.0.2 on the owner's Mac; nothing of the stack was started)
The image was built from `deploy/exec-proxy/` and run alone as compose runs it (`--read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true --memory 32m`, the repo's `allowlist.txt` mounted read-only) on a throwaway network; a second container sent raw proxy requests with `nc`. Generated config: `ConnectPort 443`, filter `^api\.dhan\.co:443$`; health check `nc -z` answered; process uid 100 (`tinyproxy`); memory in use 1.7 MB. Answers (first line):

| Request to the proxy | Answer |
|---|---|
| `CONNECT api.dhan.co:443` | `HTTP/1.1 200 Connection established` |
| `CONNECT example.com:443` | `403 Filtered` |
| `CONNECT sandbox.dhan.co:443` | `403 Filtered` |
| `CONNECT api-order-update.dhan.co:443` | `403 Filtered` |
| `CONNECT api.dhan.co:80` | `403 Access violation` |
| `CONNECT api.dhan.co:8443` | `403 Access violation` |
| `CONNECT API.DHAN.CO:443` | `403 Filtered` |
| `CONNECT api.dhan.co.evil.test:443` | `403 Filtered` |
| `CONNECT 1.1.1.1:443` | `403 Filtered` |
| `GET http://api.dhan.co/` (plain forward) | `403 Filtered` |
| `GET http://api.dhan.co:443/` | `403 Filtered` |
| `GET http://example.com/` | `403 Filtered` |

A list holding a malformed line made the container exit 1 (`refusing a malformed allow-list line`). No HTTP request was sent to Dhan through the tunnel (the 200 only proves the TCP route to `api.dhan.co:443`). The container, network and image were removed afterwards. **Still UNVERIFIED:** that `api.dhan.co` serves `/v2/profile` and `/v2/ip/getIP` for a live token and that the source address seen by Dhan is the registered one; both need the owner's smoke (runbook, "The live readiness route"), and until it is recorded the switch stays off.

## Owner decision (2026-10-31)
Accept B, choose D (stay unavailable), or ask for A. If B: confirm that `api.dhan.co:443` is the only live host for the two reads, and whether the proxy should be started by `--exec` automatically or only with an additional `XD_EXEC_READINESS_LIVE=true`.

## Known limits (build review, 2026-10-31)
- The proxy has no `Allow` directive: any member of `exec` or `egress` (xd-exec, xd-db, xd-api, xd-feed) could use it, but only for `api.dhan.co:443`, so it adds no reach. Not a wider pin.
- `HTTPS_PROXY` is accepted as any `http://host:port`; it comes only from compose. The port in the list is not range-checked (a bad port gives a dead rule), and where tinyproxy logs under `-d` is unverified.
- The proxy starts with `--exec` plus the switch even if `XD_EXEC_ENVIRONMENT` is not `live`; the app then does not use it.
- The sandbox `FindAsync` loop still has its own copy of the GET retry loop (not a regression).
