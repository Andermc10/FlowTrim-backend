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

## Notes

- Known deferred issues are tracked in [`docs/TECH-DEBT.md`](docs/TECH-DEBT.md).
