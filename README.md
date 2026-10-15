# FlowTrim-backend

C# / ASP.NET Core Web API for **FlowTrim** — a deterministic, transparent engine to track recurring
subscriptions, compute monthly-equivalent and annual spending, warn about upcoming renewals, and
simulate cancellation savings. Built on .NET 10 with a layered architecture.

FlowTrim is a **manual-data** personal-finance tool and a portfolio project: it does not connect to
banks, detect charges or verify usage — the user enters their own data. The Angular client lives in
[`FlowTrim-frontend`](https://github.com/Andermc10/FlowTrim-frontend).

## Features (API)

- Register, list (paged), view, edit and delete subscriptions.
- Financial summary **per currency**: monthly equivalent + annual projection (active only; currencies
  never summed).
- Upcoming charges with a server-computed countdown and alert windows (overdue/today/≤3/≤7/later).
- Cancellation simulator: potential savings at 1/12/24 months (does not persist).
- Declared usage records and cost-per-use analysis for a selected or derived period.
- Health check (API + database), consistent RFC 7807 errors, configurable CORS.

## Tech stack

- .NET 10 · ASP.NET Core Web API (minimal APIs)
- PostgreSQL 17 (local via Docker Compose)
- Entity Framework Core 10 + Npgsql, `EFCore.NamingConventions` (snake_case)
- xUnit for tests

## Architecture

Modular monolith, layered. Project references enforce the dependency rule:

- `Api` → `Application` + `Infrastructure`
- `Application` → `Domain`
- `Infrastructure` → `Application` + `Domain`
- `Domain` → nothing

Business rules live in `Domain` (pure, unit-tested); EF entities never leave the backend — the API
exposes request/response DTOs only.

## Solution layout

```
src/
  FlowTrim.Api             HTTP endpoints, DI composition, configuration
  FlowTrim.Application     use cases, contracts, DTOs
  FlowTrim.Domain          entities and business rules (depends on nothing)
  FlowTrim.Infrastructure  EF Core, DbContext, repositories, persistence
tests/
  FlowTrim.Tests           xUnit tests
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker](https://www.docker.com/) + Docker Compose

## Local database (PostgreSQL via Docker Compose)

Credentials are read from a local `.env` file that is **not** committed. Create it from the
template and adjust the values:

```bash
cp .env.example .env
```

`.env` variables:

| Variable | Purpose |
|---|---|
| `POSTGRES_USER` | database user |
| `POSTGRES_PASSWORD` | database password (local only) |
| `POSTGRES_DB` | database name |
| `POSTGRES_PORT` | host port mapped to Postgres (default `5432`) |

Start and stop the database:

```bash
docker compose up -d        # start in the background
docker compose ps           # check status (wait until "healthy")
docker compose logs -f postgres
docker compose down         # stop (data is kept in the named volume)
docker compose down -v      # stop AND delete the data volume
```

Data persists in the named volume `flowtrim_pgdata` across restarts.

> **Port conflict:** if a local PostgreSQL already listens on `5432`, set a free port in `.env`
> (e.g. `POSTGRES_PORT=5433`) and use that same port in the connection string below.

## Connection string and secrets

The connection string is **never** committed. Provide it through:

- **Development:** .NET User Secrets, under the key `ConnectionStrings:FlowTrimDb`.

  ```bash
  dotnet user-secrets set "ConnectionStrings:FlowTrimDb" \
    "Host=localhost;Port=5432;Database=flowtrim;Username=flowtrim;Password=<your-password>" \
    --project src/FlowTrim.Api
  ```

  (User Secrets live outside the repository, in your user profile.)

- **Deployment:** the environment variable `ConnectionStrings__FlowTrimDb`.

The password in the connection string must match the one in your `.env`.

## Build and test

```bash
dotnet build FlowTrim.slnx
dotnet test FlowTrim.slnx
```

## Health check

`GET /api/health` reports API and database availability:

- `200 OK` with `{ "status": "Healthy", "api": "Healthy", "database": "Healthy" }` when the API is up and PostgreSQL is reachable.
- `503 Service Unavailable` with `{ "status": "Unhealthy", ... }` when the database does not respond. No internal details are exposed; the error is logged server-side only.

Run the API and query it:

```bash
docker compose up -d
dotnet run --project src/FlowTrim.Api --launch-profile http   # http://localhost:5214
# in another terminal:
curl http://localhost:5214/api/health
```

## Database migrations

EF Core migrations are applied with the locally pinned `dotnet-ef` tool (restore it once with
`dotnet tool restore`). The connection string is read from User Secrets, so pass the API as the
startup project:

```bash
dotnet tool restore
dotnet ef database update \
  --project src/FlowTrim.Infrastructure \
  --startup-project src/FlowTrim.Api
```

Migrations are committed with the code that needs them and are **never applied automatically** —
review the generated SQL, then run the command above.

## Subscriptions API

| Method | Route | Success | Errors |
|---|---|---|---|
| POST | `/api/subscriptions` | `201` + resource (`Location` header) | `400` |
| GET | `/api/subscriptions?page=&pageSize=` | `200` paged list | — |
| GET | `/api/subscriptions/{id}` | `200` | `404` |
| PUT | `/api/subscriptions/{id}` | `200` | `400`, `404` |
| DELETE | `/api/subscriptions/{id}` | `204` | `404` |

The list is ordered by next billing date then name; `page` defaults to 1 and `pageSize` to 20
(max 100). `PUT` is a full replace of editable fields (including `status`). Invalid input returns
`400` (`ValidationProblemDetails`) and nothing is persisted.

`GET /api/summary` returns the financial summary **per currency** (monthly equivalent, annual
projection, active count) over active subscriptions — currencies are never summed together; empty
when there are none.

`GET /api/subscriptions/upcoming` returns active subscriptions ordered by next billing date, each
tagged with `daysUntilCharge` and an `alertWindow` (`Overdue` / `Today` / `DueWithin3Days` /
`DueWithin7Days` / `Later`), plus the server `referenceDate` (computed in America/Bogota, UTC−5).
The countdown is computed server-side so the client never recomputes "today".

`POST /api/subscriptions/{id}/usage-records` records a **declared** usage (`usageDate`, `usageCount ≥ 0`,
optional `notes`) for an existing subscription — `201` / `400` / `404` (unknown subscription).
`GET /api/subscriptions/{id}/usage-records?from=&to=` lists its usage (ordered by date desc, optional
date range) — `200` / `404`. Usage is declared by the user, not verified.

`GET /api/subscriptions/{id}/cost-per-use?from=&to=` returns the period cost, total declared uses and
the rounded cost per use — `200` / `404`. If there are no records or total uses are zero, the response
is `calculable: false` with `costPerUse: null`; it never divides by zero. When both bounds are omitted,
the period is derived from the minimum and maximum usage dates.

`POST /api/simulations` simulates cancelling a selection of subscriptions and returns, **per currency**,
the current/selected/projected monthly equivalent and the potential savings over the given horizons
(default `1, 12, 24` months). It **persists nothing**; `400` on a non-positive horizon. The response
carries the assumptions (constant prices, no penalties/taxes, not guaranteed).

## Errors

A global exception handler returns RFC 7807 `ProblemDetails` consistently: `400`
`ValidationProblemDetails` for validation (including domain invariants), `404` for unknown resources,
and a generic `500` for unexpected failures — never a stack trace, connection string or any internal
detail (the real error is logged server-side). It runs in all environments (replacing the Development
exception page) so behavior is consistent.

## CORS

Cross-origin requests are allowed only from the origins in `Cors:AllowedOrigins` (default
`http://localhost:4200`, the Angular dev server) — never all origins. Override per environment via
configuration, e.g. the `Cors__AllowedOrigins__0` environment variable. In Development, HTTPS
redirection is disabled so the HTTP dev flow and CORS preflight work cleanly.

## Known limitations

- **Manual data only** — no bank integration, no automatic charge detection, no usage verification.
- **No authentication / multi-user** yet — data is not isolated per user (planned, HU-12, post-MVP).
- **No currency conversion** — totals are always grouped per currency, never combined.
- **Local demo** — PostgreSQL runs via Docker Compose; there is no public deployment yet.
- Estimates (projections, savings) assume constant prices and carry their assumptions; they are never
  presented as guarantees.

See [`docs/RELEASE-REVIEW.md`](docs/RELEASE-REVIEW.md) for the pre-release quality/security review.

## Notes

- Known deferred technical items are tracked in [`docs/TECH-DEBT.md`](docs/TECH-DEBT.md).
- Planning, user stories and specs live in the `FlowTrim-tracker` repository.
