# FlowTrim-backend

C# / ASP.NET Core Web API for **FlowTrim** — a deterministic engine to track recurring
subscriptions, compute monthly-equivalent and annual spending, warn about upcoming
renewals, and simulate cancellation savings. Built on .NET 10 with a layered architecture.

## Tech stack

- .NET 10 · ASP.NET Core Web API
- PostgreSQL 17 (local via Docker Compose)
- Entity Framework Core + Npgsql (added in later tasks)
- xUnit for tests

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
dotnet run --project src/FlowTrim.Api
# in another terminal (replace the port with the one printed by the API):
curl http://localhost:5080/api/health
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

## CORS

Cross-origin requests are allowed only from the origins in `Cors:AllowedOrigins` (default
`http://localhost:4200`, the Angular dev server) — never all origins. Override per environment via
configuration, e.g. the `Cors__AllowedOrigins__0` environment variable. In Development, HTTPS
redirection is disabled so the HTTP dev flow and CORS preflight work cleanly.

## Notes

- Known deferred issues are tracked in [`docs/TECH-DEBT.md`](docs/TECH-DEBT.md).
