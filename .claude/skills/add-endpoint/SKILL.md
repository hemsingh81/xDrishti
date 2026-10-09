---
name: add-endpoint
description: Add or change an HTTP endpoint (module, JSON context, contract regeneration, integration test). Use whenever the API surface changes.
---

# Add an endpoint

1. In `backend/src/XDrishti.Api/Endpoints/<Module>Endpoints.cs` implement `IEndpointModule` (internal sealed). Group
   with `api.MapGroup("/<resource>").WithTags("<Module>")`; every route gets `.WithName("<OperationId>")` and
   `.WithSummary(...)`. Return `TypedResults`; the handler is injected as `IQueryHandler<,>` / `ICommandHandler<>`.
   Routes: `/api/<module>/<resource>`, plural kebab-case nouns. No business logic in the endpoint.
2. Map `Result` failures to problem details by `ErrorKind` (NotFound → 404, Validation → 400, Conflict → 409); never leak
   exceptions. No shared mapper exists yet — the first endpoint that needs one adds it in `Api/Infrastructure/` and
   every later endpoint reuses it.
3. Register the module in `ApiSetup.AddApi` (`services.AddSingleton<IEndpointModule, <Module>Endpoints>()`).
4. Add every request/response type to `ApiJsonContext` (`[JsonSerializable(typeof(...))]`).
5. Integration test in `backend/tests/XDrishti.Api.IntegrationTests/<Module>/` (`[Collection(ApiTestSuite.Name)]`):
   status codes, body shape, a problem+json failure path.
6. Run `scripts/gen-api.sh` — it refreshes `backend/contracts/XDrishti.Api.json` and
   `frontend/src/shared/api/schema.d.ts`. Commit both with the change; never edit them by hand.
7. Frontend: `queryOptions` in `features/<x>/api/` using `api.GET/POST` + `unwrap`; add an MSW handler in tests.
8. If the path is new at the proxy level (e.g. `/hubs`), add it to `deploy/caddy/Caddyfile`.
