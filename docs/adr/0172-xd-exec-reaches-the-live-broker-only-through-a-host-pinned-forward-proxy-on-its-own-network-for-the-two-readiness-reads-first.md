# 0172 - xd-exec reaches the live broker only through a host-pinned forward proxy on its own network, for the two readiness reads first

- **Status:** Proposed (owner decision needed; it is a network and security change, so nothing is built from it until it is Accepted).
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

## Owner decision needed
Accept B, choose D (stay unavailable), or ask for A. If B: confirm that `api.dhan.co:443` is the only live host for the two reads, and whether the proxy should be started by `--exec` automatically or only with an additional `XD_EXEC_READINESS_LIVE=true`.
