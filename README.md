# thediscussion

Application for structured discussions. Product requirements live in
[`docs/core-requirements.md`](docs/core-requirements.md).

## Layout

| Path | What it is |
| --- | --- |
| `src/TheDiscussion.Core` | Domain model, argument status engine and storage interfaces. No storage technology. |
| `src/TheDiscussion.Api` | ASP.NET Core REST API (application tier). The storage provider is chosen in its composition root (`Composition/StorageComposition.cs`) from configuration. |
| `src/TheDiscussion.Storage.InMemory` | In-memory storage provider (local default and tests). Data is lost when the API stops. |
| `src/TheDiscussion.Storage.Postgres` | PostgreSQL storage provider (EF Core + Npgsql) with its migrations. |
| `tests/` | xUnit test projects: architecture rules, storage contract suite, API integration. |
| `web/` | React + TypeScript + Vite front end. Talks to the API only through REST. |
| `.github/workflows/ci.yml` | GitHub Actions CI: .NET build and tests (with a PostgreSQL service container), web lint, type-check, tests and build. |

## Prerequisites

- .NET SDK 8 (pinned by `global.json`, any 8.0 feature band ≥ 8.0.100)
- Node.js 22 and npm
- Optional: PostgreSQL 13 or later, to run the API or the storage tests against PostgreSQL

## Run the API

### In-memory storage (default)

```sh
dotnet run --project src/TheDiscussion.Api --launch-profile InMemory
```

### PostgreSQL storage

Point the API at a database it may create tables in. Pending migrations are applied at startup
(turn that off with `Storage__Postgres__MigrateOnStartup=false`).

```sh
# bash
export ConnectionStrings__TheDiscussion="Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

```powershell
# PowerShell
$env:ConnectionStrings__TheDiscussion = "Host=localhost;Port=5432;Database=thediscussion;Username=thediscussion;Password=<password>"
dotnet run --project src/TheDiscussion.Api --launch-profile Postgres
```

Configuration keys: `Storage:Provider` (`InMemory` or `Postgres`), `ConnectionStrings:TheDiscussion`,
`Storage:Postgres:MigrateOnStartup`. Do not commit connection strings; pass them through the
environment or user secrets.

The API listens on <http://localhost:5080>. `GET /health` returns 200 with storage reachability
(503 when storage cannot be reached). Logs are written to the console as one JSON object per line,
with one entry per request (method, route, status code, duration).

## Run the front end

```sh
cd web
npm ci
npm run dev
```

Open <http://localhost:5173>. The dev server proxies `/api` and `/health` to the API at
`http://localhost:5080` (override with the `API_PROXY_TARGET` environment variable).

## Tests

.NET (from the repository root):

```sh
dotnet test
```

The storage contract suite runs against the in-memory provider every time. It runs against
PostgreSQL only when `THEDISCUSSION_TEST_POSTGRES` holds a connection string for a user that may
create databases; each run creates a throwaway database, applies the migrations and drops it.
Without the variable those tests are reported as skipped and the reason is printed. CI sets the
variable and `THEDISCUSSION_REQUIRE_POSTGRES_TESTS=true`, which turns a missing database into a
failure.

```sh
THEDISCUSSION_TEST_POSTGRES="Host=localhost;Username=postgres;Password=<password>" dotnet test
```

Web (from `web/`):

```sh
npm ci && npm run lint && npm test && npm run build
```

`npm run typecheck` runs the TypeScript compiler on its own (`npm run build` also type-checks).

## Database migrations

The schema is managed with EF Core migrations in `src/TheDiscussion.Storage.Postgres/Migrations`.
After changing `DiscussionDbContext`, add a migration with the pinned local tool:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/TheDiscussion.Storage.Postgres --output-dir Migrations
```

A test fails when the model and the latest migration snapshot disagree.
