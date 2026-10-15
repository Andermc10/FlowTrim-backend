# Pre-release quality & security self-review — v0.1.0

Date: 2026-10-15. Scope: FlowTrim MVP backend + Angular frontend. This is a documented self-review;
a deeper audit can be run with the repository's dedicated `security-review` agent (`/security-review`).

## Secrets & configuration
- [x] No connection strings or passwords in Git. Dev uses .NET User Secrets (`ConnectionStrings:FlowTrimDb`); deploy uses env vars.
- [x] `.env` is git-ignored; only `.env.example` (placeholder values) is committed.
- [x] `500` responses never include the connection string or any config value (verified live in HU-09).

## Error handling & information disclosure
- [x] Global exception handler returns RFC 7807 `ProblemDetails`; `500` is generic with no stack trace (all environments).
- [x] Domain invariant violations → `400` `ValidationProblemDetails`; unknown resources → `404`.
- [x] Health check reports DB availability without exposing internals (`503` when down).

## Input validation
- [x] Validation at the API boundary (presence/parse) **and** domain invariants (name, amount > 0, currency format, enums).
- [x] Coherent status codes: `200/201/204/400/404/500`.

## CORS & transport
- [x] CORS allows only configured origins (`Cors:AllowedOrigins`, default the Angular dev origin) — never all origins.
- [x] HTTPS redirection disabled only in Development (HTTP dev flow); kept in non-Development.

## Data & privacy
- [x] No real user data or credentials in tests or fixtures; the local DB holds sample rows only.
- [x] No authentication yet → **do not expose publicly with private data** until HU-12 (documented limitation).

## Tests & build
- [x] `dotnet build` 0 errors; `dotnet test` 70 passed. Frontend `npm run build` + `npm test` (10) green.
- [x] Business rules are unit-tested in the pure domain; live E2E recorded in `FlowTrim-tracker/docs/E2E-ACCEPTANCE.md`.

## Known accepted items
- Transitive `Microsoft.OpenApi` advisory (NU1903) — tracked as TD-001 in `docs/TECH-DEBT.md`, deferred.
- Angular pinned to 21 + `legacy-peer-deps` — frontend `docs/TECH-DEBT.md` (TD-001/TD-002).

## Follow-ups (not blocking the MVP tag)
- Authentication & per-user isolation (HU-12). Usage & cost-per-use (HU-05/06). Public deployment (HU-11 #27).
- Resolve TD-001 (OpenApi) when the Swagger/OpenAPI surface is wired.
