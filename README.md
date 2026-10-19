# FlowTrim-backend

[![Backend CI](https://github.com/Andermc10/FlowTrim-backend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Andermc10/FlowTrim-backend/actions/workflows/ci.yml)

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
- JWT/OIDC authentication with server-side owner isolation for private financial data.
- FlowTrim Insights: short questions about your own data, answered from deterministic tool results (see below).

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

## JWT authentication (Auth0)

Private API routes require a bearer access token issued by **Auth0** (any standards-compliant OIDC
provider works the same way). The API validates signature (provider JWKS), issuer, audience and lifetime
with `JwtBearer`; it never issues passwords or validates tokens manually.

Auth0 setup (once per tenant):

1. **APIs → Create API** — identifier = your audience (e.g. `https://api.flowtrim.dev`), signing RS256.
2. **Applications → Single Page Application** — used by the Angular client (see the frontend README).

Configure the API through User Secrets locally or environment variables in deployment:

```bash
dotnet user-secrets set "Authentication:Authority" "https://<tenant>.<region>.auth0.com/" --project src/FlowTrim.Api
dotnet user-secrets set "Authentication:Audience" "https://api.flowtrim.dev" --project src/FlowTrim.Api
```

Equivalent deployment variables are `Authentication__Authority` and `Authentication__Audience`.
Both are **required in every environment**: the API refuses to start without them (validated on start,
so `dotnet ef` still works). `Authentication:RequireHttpsMetadata` defaults to `true`.

Authorization rules:

- The owner key is the token's stable `sub` claim; a token without `sub` gets `403`.
- The client never sends an owner identifier; repositories apply the owner predicate server-side.
- Another owner's subscription (or its usage) answers `404`, exactly like a missing id.
- `GET /api/health` stays public; every other route returns `401` without a valid token.

`AddUserOwnership` back-fills pre-authentication rows with the inaccessible owner `legacy-owner` and then
drops the column default. For a clean local demo, reset the database volume:

```bash
docker compose down -v && docker compose up -d --wait
dotnet ef database update --project src/FlowTrim.Infrastructure --startup-project src/FlowTrim.Api
```

## Build and test

```bash
dotnet build FlowTrim.slnx
dotnet test FlowTrim.slnx
```

**Continuous integration:** `.github/workflows/ci.yml` runs restore, Release build and all tests on every
pull request to `main` and every push to `main`. It needs no secrets (no Auth0, no database). `main` is
protected: a pull request cannot be merged while the `build-and-test` check is failing.

Tests include HTTP integration tests (`tests/FlowTrim.Tests/Api`) that host the real API with
`WebApplicationFactory`, a fake authentication scheme and in-memory repositories (no database, no real
tokens), covering `401`/`403`, owner isolation on every private route and fail-fast configuration.

### Database integration tests (PostgreSQL)

`tests/FlowTrim.Tests/Integration` runs against a **real, disposable PostgreSQL** started by
[Testcontainers](https://dotnet.testcontainers.org/) (`postgres:17-alpine`): one container per test run,
the real EF Core migrations applied on start, tables truncated before each test, container removed at the
end. It never touches your local `flowtrim` database and needs no credentials (Testcontainers generates
them; authentication uses the fake test scheme).

They cover the migrated schema and constraints, `HasPendingModelChanges()`, the real repositories (owner
filters, ordering, paging, date ranges, decimal/date round-trips) and the API end to end over the database.

- **Requirement:** Docker running (Docker Desktop locally; GitHub-hosted runners already have it).
- **Without Docker:** run only the fast tests with `dotnet test FlowTrim.slnx --filter "Category!=Integration"`.
- **First run** downloads the PostgreSQL image and Testcontainers' `ryuk` cleanup image. If Docker Desktop
  reports `No such image: testcontainers/ryuk…`, pull it once with `docker pull testcontainers/ryuk:0.14.0`.

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

`GET /api/analytics?months=12` (`months` 1–24) returns, **per currency** of the owner's active subscriptions:
the monthly total, spending by category (with share %, missing category = `Uncategorized`), the top 5
subscriptions and the **expected charges per calendar month** from the current month (America/Bogota):
monthly plans every month from their billing month, yearly plans only in their billing month and every 12
months. These are expected charges from user-entered data, not confirmed payments; there is no charge history.

`GET /api/reports/subscriptions.csv` downloads **all** the owner's subscriptions (active and inactive) as an
RFC 4180 CSV attachment (`text/csv; charset=utf-8` with BOM so spreadsheets show accents correctly; columns
`name, category, amount, currency, billing_cycle, monthly_equivalent, next_billing_date, status`). Text cells that
start with `= + - @`, tab or CR are prefixed with `'` to prevent spreadsheet formula injection.

## Errors

A global exception handler returns RFC 7807 `ProblemDetails` consistently: `400`
`ValidationProblemDetails` for validation (including domain invariants), `404` for unknown resources,
and a generic `500` for unexpected failures — never a stack trace, connection string or any internal
detail (the real error is logged server-side). It runs in all environments (replacing the Development
exception page) so behavior is consistent.

## CORS

Browsers may call the API only from the origins listed in `Cors:AllowedOrigins`. There is **no default
in code**: Development sets `http://localhost:4200` in `appsettings.Development.json`; every other
environment must set them explicitly (`Cors__AllowedOrigins__0`, `__1`, …).

Outside Development the API refuses to start unless each origin is an exact `https://` origin (scheme and
host, optional port — no path, trailing slash or `*`). The policy allows `GET, POST, PUT, DELETE`, the
headers `Accept, Content-Type, Authorization`, and no cookies. Error messages name the setting, never its
value. HTTPS redirection and HSTS are enabled outside Development.

## Logs and correlation

Every request gets a **correlation id**: the caller's `X-Correlation-ID` header when it is safe
(1–64 chars of `A-Z a-z 0-9 . _ -`), otherwise a new 32-character hex id. It is returned in the
`X-Correlation-ID` response header (exposed to the browser through CORS), included as `correlationId` in
every `ProblemDetails` error body, and attached as the `CorrelationId` scope to every log line of the request.

Each request writes one line: `HTTP {Method} {Route} responded {StatusCode} in {ElapsedMs} ms`, using the
route template (e.g. `/api/subscriptions/{id:guid}`) and never the query string; `/api/health` logs at
`Debug`. Outside Development logs are JSON (one object per line, UTC); Development uses the readable console.

Never logged: `Authorization` header or tokens, cookies, request/response bodies, query strings, connection
strings, the user's `sub`, subscription names or amounts. To trace a failure, search the logs for the
reference shown to the user (see the tracker's `docs/OPERATIONS.md`).

## AI provider (FlowTrim Insights groundwork)

The application talks to language models only through `IChatModel` (HU-33). Two implementations exist:

- **`Fake`** (default) — deterministic and offline. Used by tests, CI and anyone who clones the repository; no key needed.
- **`OpenAiCompatible`** — any `/chat/completions` endpoint in the OpenAI format (Groq, OpenRouter,
  Gemini's compatible endpoint). Switching provider is a configuration change.

The local setup uses **Groq**'s free tier (no card; at the time of writing 1,000 requests/day and 8,000 tokens/minute
for `openai/gpt-oss-120b`; tool calling verified). GitHub Models, the first choice, was retired on 2026-07-30.
Create a key at console.groq.com and keep it in User Secrets (never in `appsettings*.json`, which are versioned):

```bash
dotnet user-secrets set "Insights:Provider:Kind" "OpenAiCompatible" --project src/FlowTrim.Api
dotnet user-secrets set "Insights:Provider:BaseUrl" "https://api.groq.com/openai/v1" --project src/FlowTrim.Api
dotnet user-secrets set "Insights:Provider:Model" "openai/gpt-oss-120b" --project src/FlowTrim.Api
dotnet user-secrets set "Insights:Provider:ApiKey" "<your-token>" --project src/FlowTrim.Api
```

Failures become controlled errors (`Timeout`, `RateLimited`, `Unauthorized`, `Unavailable`, `InvalidResponse`,
`InputTooLarge`); logs record provider, model, outcome, duration and token counts — never the key, questions or answers.
Free tiers may retain prompts: use demo data only. The model only explains results of deterministic C# tools (EP-05).

### Insights questions (HU-35)

`POST /api/insights/questions` with `{ "question": "..." }` (1–500 characters, authenticated). The model may call the
five read-only tools of HU-34 (summary, analytics, upcoming charges, cancellation simulation, cost per use) for the
signed-in owner — at most 2 tool rounds and 3 model calls per question — and then explains the results.

```json
{
  "status": "answered",
  "answer": "If you cancel Netflix you could save 466,800 COP in 12 months...",
  "figures": [{ "kind": "savings", "label": "Potential savings", "value": 466800, "currency": "COP", "months": 12 }],
  "assumptions": ["The simulation does not cancel anything and savings are not guaranteed."],
  "sources": ["simulator"],
  "toolsUsed": ["simulate_cancellation"]
}
```

- `figures` are built by the backend from the tool results, never from the model's text. `kind` is a stable key for
  translations; `subject` (optional) is a subscription or category name; `months` (savings horizon) and `date`
  (`yyyy-MM-dd`, expected charge date) are typed so clients format them in their own language.
- `status`: `answered`, `no_data` (tools ran but found nothing; if every tool call was rejected the model's text is
  replaced by a fixed message), or `unsupported` (no tool was used: the model's
  text is replaced by a fixed message so no invented number reaches the user).
- Limits: 5 questions per minute per user (`429` + `Retry-After`) and a 45-second budget per question.
- Provider problems: `504` (`insights_timeout`), `429` (`insights_rate_limited`), `502` (`insights_unavailable`).
- Nothing is stored; logs record status, tools, model calls, tokens and duration only.

## Configuration reference

Every setting the API reads. Development values come from `appsettings.Development.json` and User
Secrets; deployments use environment variables (or the host's secret store). Placeholders only.

| Setting | Environment variable | Required | Secret | Example |
|---|---|---|---|---|
| `ConnectionStrings:FlowTrimDb` | `ConnectionStrings__FlowTrimDb` | Always | **Yes** | `Host=<host>;Port=5432;Database=flowtrim;Username=<user>;Password=<password>` |
| `Authentication:Authority` | `Authentication__Authority` | Always | No | `https://<tenant>.<region>.auth0.com/` |
| `Authentication:Audience` | `Authentication__Audience` | Always | No | `https://api.flowtrim.dev` |
| `Authentication:RequireHttpsMetadata` | `Authentication__RequireHttpsMetadata` | No (default `true`) | No | `true` |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` (`__1`, …) | Outside Development | No | `https://<frontend-host>` |
| `AllowedHosts` | `AllowedHosts` | No (default `*`) | No | `<api-host>` |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | Deployments | No | `Production` |
| `Insights:Provider:Kind` | `Insights__Provider__Kind` | No (default `Fake`) | No | `Fake` or `OpenAiCompatible` |
| `Insights:Provider:BaseUrl` | `Insights__Provider__BaseUrl` | With `OpenAiCompatible` | No | `https://api.groq.com/openai/v1` |
| `Insights:Provider:Model` | `Insights__Provider__Model` | With `OpenAiCompatible` | No | `openai/gpt-oss-120b` |
| `Insights:Provider:ApiKey` | `Insights__Provider__ApiKey` | With `OpenAiCompatible` | **Yes** | `<provider-token>` |
| `Insights:Provider:TimeoutSeconds` / `MaxOutputTokens` / `MaxInputCharacters` | `Insights__Provider__…` | No (20 / 400 / 12000) | No | `20` |
| `Insights:RequestTimeoutSeconds` | `Insights__RequestTimeoutSeconds` | No (default 45, 5–120) | No | `45` |
| `Insights:RateLimit:PermitLimit` / `WindowSeconds` | `Insights__RateLimit__…` | No (5 per 60 s; 1–100 / 10–3600) | No | `5` |

Production logging (`appsettings.json`): `Default=Information`, `Microsoft.AspNetCore`,
`Microsoft.EntityFrameworkCore` and `Npgsql` at `Warning`, so SQL text is never written to production logs.
Development keeps SQL command logs at `Information` for learning and debugging.

## Known limitations

- **Manual data only** — no bank integration, no automatic charge detection, no usage verification.
- **Auth0 tenant required** — the API does not start until `Authentication:Authority` and
  `Authentication:Audience` are configured.
- **No currency conversion** — totals are always grouped per currency, never combined.
- **Local demo** — PostgreSQL runs via Docker Compose; there is no public deployment yet.
- Estimates (projections, savings) assume constant prices and carry their assumptions; they are never
  presented as guarantees.

See [`docs/RELEASE-REVIEW.md`](docs/RELEASE-REVIEW.md) for the pre-release quality/security review.

## Notes

- Known deferred technical items are tracked in [`docs/TECH-DEBT.md`](docs/TECH-DEBT.md).
- Planning, user stories and specs live in the `FlowTrim-tracker` repository.
